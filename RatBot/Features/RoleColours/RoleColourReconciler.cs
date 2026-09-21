using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourReconciler(IDbContextFactory<BotDbContext> dbContextFactory, ILogger logger)
{
    private const int GuildReconciliationConcurrency = 2;
    private readonly ILogger _logger = logger.ForContext<RoleColourReconciler>();

    public async Task<bool> ReconcileMemberAsync(SocketGuild guild, ulong userId, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);
        ImmutableArray<RoleColourOption> options = (
            await db.RoleColourOptions.AsNoTracking().Where(option => option.GuildId == guild.Id).ToArrayAsync(ct)
        ).ToImmutableArray();
        MemberColourPreference? preference = await db
            .MemberColourPreferences.AsNoTracking()
            .SingleOrDefaultAsync(p => p.GuildId == guild.Id && p.UserId == userId, ct);
        SocketGuildUser? member = guild.GetUser(userId);

        if (member is null)
        {
            _logger.Debug("role_colour_reconcile member_missing guild_id={GuildId} user_id={UserId}", guild.Id, userId);
            return false;
        }

        return await ReconcileMemberAsync(guild, member, options, preference, ct);
    }

    public async Task<int> ReconcileGuildAsync(SocketGuild guild, CancellationToken ct)
    {
        await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);
        ImmutableArray<RoleColourOption> options = (
            await db.RoleColourOptions.AsNoTracking().Where(option => option.GuildId == guild.Id).ToArrayAsync(ct)
        ).ToImmutableArray();
        Dictionary<ulong, MemberColourPreference> preferences = await db
            .MemberColourPreferences.AsNoTracking()
            .Where(preference => preference.GuildId == guild.Id)
            .ToDictionaryAsync(preference => preference.UserId, ct);

        HashSet<ulong> configuredRoleIds = options.SelectMany(option => new[] { option.SourceRoleId, option.DisplayRoleId }).ToHashSet();
        ImmutableArray<SocketGuildUser> members = guild
            .Users.Where(member => preferences.ContainsKey(member.Id) || member.Roles.Any(role => configuredRoleIds.Contains(role.Id)))
            .ToImmutableArray();

        int changedCount = 0;
        ParallelOptions parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = GuildReconciliationConcurrency, CancellationToken = ct };

        await Parallel.ForEachAsync(
            members,
            parallelOptions,
            async (member, cancellationToken) =>
            {
                preferences.TryGetValue(member.Id, out MemberColourPreference? preference);

                if (await ReconcileMemberAsync(guild, member, options, preference, cancellationToken))
                    Interlocked.Increment(ref changedCount);
            }
        );

        return changedCount;
    }

    internal static ulong? SelectTargetDisplayRole(
        IReadOnlyCollection<ulong> currentRoleIds,
        MemberColourPreference? preference,
        IReadOnlyCollection<RoleColourOption> options,
        Func<ulong, int?> getRolePosition
    )
    {
        if (preference?.Kind == MemberColourPreferenceKind.NoColour)
            return null;

        ImmutableArray<RoleColourOption> enabledOptions = options.Where(option => option.IsEnabled).ToImmutableArray();

        if (preference is { Kind: MemberColourPreferenceKind.ConfiguredOption, SelectedOptionId: not null })
        {
            RoleColourOption? selected = enabledOptions.SingleOrDefault(option => option.OptionId == preference.SelectedOptionId.Value);

            if (selected is not null && currentRoleIds.Contains(selected.SourceRoleId))
                return selected.DisplayRoleId;
        }

        return enabledOptions
            .Where(option => currentRoleIds.Contains(option.SourceRoleId))
            .OrderByDescending(option => getRolePosition(option.SourceRoleId) ?? int.MinValue)
            .ThenBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .Select(option => (ulong?)option.DisplayRoleId)
            .FirstOrDefault();
    }

    internal static (ImmutableArray<ulong> Add, ImmutableArray<ulong> Remove) CalculateRoleDiff(
        IReadOnlyCollection<ulong> currentRoleIds,
        IReadOnlyCollection<ulong> configuredDisplayRoleIds,
        ulong? targetDisplayRoleId
    )
    {
        ImmutableArray<ulong> add =
            targetDisplayRoleId.HasValue && !currentRoleIds.Contains(targetDisplayRoleId.Value)
                ? ImmutableArray.Create(targetDisplayRoleId.Value)
                : ImmutableArray<ulong>.Empty;
        ImmutableArray<ulong> remove = currentRoleIds
            .Where(roleId => configuredDisplayRoleIds.Contains(roleId) && roleId != targetDisplayRoleId)
            .Distinct()
            .ToImmutableArray();

        return (add, remove);
    }

    private async Task<bool> ReconcileMemberAsync(
        SocketGuild guild,
        SocketGuildUser member,
        ImmutableArray<RoleColourOption> options,
        MemberColourPreference? preference,
        CancellationToken ct
    )
    {
        ImmutableArray<ulong> currentRoleIds = member.Roles.Select(role => role.Id).ToImmutableArray();
        ulong? targetDisplayRoleId = SelectTargetDisplayRole(currentRoleIds, preference, options, roleId => guild.GetRole(roleId)?.Position);
        ImmutableArray<ulong> displayRoleIds = options.Select(option => option.DisplayRoleId).ToImmutableArray();
        (ImmutableArray<ulong> add, ImmutableArray<ulong> remove) = CalculateRoleDiff(currentRoleIds, displayRoleIds, targetDisplayRoleId);

        if (add.IsEmpty && remove.IsEmpty)
            return false;

        RequestOptions requestOptions = new RequestOptions { CancelToken = ct };

        foreach (ulong roleId in remove)
            await member.RemoveRoleAsync(roleId, requestOptions);

        foreach (ulong roleId in add)
            await member.AddRoleAsync(roleId, requestOptions);

        _logger.Debug(
            "role_colour_reconcile applied guild_id={GuildId} user_id={UserId} target_dcr={TargetDcr} added=[{Added}] removed=[{Removed}]",
            guild.Id,
            member.Id,
            targetDisplayRoleId,
            string.Join(',', add),
            string.Join(',', remove)
        );

        return true;
    }
}
