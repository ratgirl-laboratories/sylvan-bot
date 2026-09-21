using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourOperations(BotDbContext db)
{
    public async Task<ImmutableArray<RoleColourOption>> ListEligibleOptionsAsync(
        ulong guildId,
        IReadOnlyCollection<ulong> currentMemberRoleIds,
        CancellationToken ct
    ) =>
    (
        await db
            .RoleColourOptions.AsNoTracking()
            .Where(option => option.GuildId == guildId
                             && option.IsEnabled
                             && currentMemberRoleIds.Contains(option.SourceRoleId))
            .OrderBy(option => option.Label)
            .ThenBy(option => option.NormalisedKey)
            .ToArrayAsync(ct)
    ).ToImmutableArray();

    public async Task<ImmutableArray<RoleColourOption>> ListConfiguredOptionsAsync(
        ulong guildId,
        bool includeDisabled,
        CancellationToken ct
    ) =>
    (
        await db
            .RoleColourOptions.AsNoTracking()
            .Where(option => option.GuildId == guildId && (includeDisabled || option.IsEnabled))
            .OrderBy(option => option.Key)
            .ToArrayAsync(ct)
    ).ToImmutableArray();

    public async Task<ErrorOr<RoleColourOption>> SelectOptionAsync(
        ulong guildId,
        ulong userId,
        RoleColourOption.Id selectedOptionId,
        IReadOnlyCollection<ulong> currentMemberRoleIds,
        CancellationToken ct
    )
    {
        RoleColourOption? option = await db
            .RoleColourOptions.AsNoTracking()
            .SingleOrDefaultAsync(o => o.GuildId == guildId && o.OptionId == selectedOptionId, ct);

        if (option is null || !option.IsEnabled || !currentMemberRoleIds.Contains(option.SourceRoleId))
            return Error.Validation(description: "That colour is no longer available to you.");

        MemberColourPreference? preference = await db.MemberColourPreferences.SingleOrDefaultAsync(
            p => p.GuildId == guildId && p.UserId == userId,
            ct
        );

        if (preference is null)
            await db.MemberColourPreferences.AddAsync(
                MemberColourPreference.CreateForOption(guildId, userId, selectedOptionId),
                ct);
        else
            preference.SelectOption(selectedOptionId);

        await db.SaveChangesAsync(ct);
        return option;
    }

    public async Task SelectNoColourAsync(ulong guildId, ulong userId, CancellationToken ct)
    {
        MemberColourPreference? preference = await db.MemberColourPreferences.SingleOrDefaultAsync(
            p => p.GuildId == guildId && p.UserId == userId,
            ct
        );

        if (preference is null)
            await db.MemberColourPreferences.AddAsync(MemberColourPreference.CreateNoColour(guildId, userId), ct);
        else
            preference.SelectNoColour();

        await db.SaveChangesAsync(ct);
    }

    public async Task<ErrorOr<RoleColourOption>> AddMappingAsync(
        ulong guildId,
        string key,
        string label,
        ulong sourceRoleId,
        ulong displayRoleId,
        CancellationToken ct
    )
    {
        ErrorOr<RoleColourOption> optionResult = RoleColourOption.Create(
            guildId,
            key,
            label,
            sourceRoleId,
            displayRoleId);

        if (optionResult.IsError)
            return optionResult.Errors;

        RoleColourOption option = optionResult.Value;

        List<RoleColourOption> existing =
            await db.RoleColourOptions.AsNoTracking().Where(o => o.GuildId == guildId).ToListAsync(ct);

        if (existing.Exists(o => string.Equals(o.NormalisedKey, option.NormalisedKey, StringComparison.Ordinal)))
            return Error.Conflict(description: $"Colour option `{option.Key}` is already registered.");

        if (existing.Exists(o => o.SourceRoleId == sourceRoleId))
            return Error.Conflict(description: "Source role is already mapped to a colour option.");

        if (existing.Exists(o => o.DisplayRoleId == displayRoleId))
            return Error.Conflict(description: "Display role is already mapped to a colour option.");

        await db.RoleColourOptions.AddAsync(option, ct);
        await db.SaveChangesAsync(ct);
        return option;
    }

    public async Task<ErrorOr<(RoleColourOption Option, bool Created)>> UpsertMappingAsync(
        ulong guildId,
        string key,
        string label,
        ulong sourceRoleId,
        ulong displayRoleId,
        CancellationToken ct
    )
    {
        ErrorOr<RoleColourOption> optionResult = RoleColourOption.Create(
            guildId,
            key,
            label,
            sourceRoleId,
            displayRoleId);

        if (optionResult.IsError)
            return optionResult.Errors;

        RoleColourOption candidate = optionResult.Value;
        List<RoleColourOption> existing = await db.RoleColourOptions.Where(o => o.GuildId == guildId).ToListAsync(ct);
        RoleColourOption? option = existing.SingleOrDefault(o => o.SourceRoleId == sourceRoleId);

        if (existing.Exists(o =>
                string.Equals(o.NormalisedKey, candidate.NormalisedKey, StringComparison.Ordinal)
                && o.OptionId != option?.OptionId))
            return Error.Conflict(description: $"Colour option `{candidate.Key}` is already registered.");

        if (existing.Exists(o => o.DisplayRoleId == displayRoleId && o.OptionId != option?.OptionId))
            return Error.Conflict(description: "Display role is already mapped to a colour option.");

        if (option is null)
        {
            await db.RoleColourOptions.AddAsync(candidate, ct);
            await db.SaveChangesAsync(ct);
            return (candidate, true);
        }

        ErrorOr<Success> updateResult = option.Update(candidate.Key, candidate.Label, displayRoleId);

        if (updateResult.IsError)
            return updateResult.Errors;

        await db.SaveChangesAsync(ct);
        return (option, false);
    }

    public async Task<ErrorOr<RoleColourOption>> DeleteMappingAsync(ulong guildId, string key, CancellationToken ct)
    {
        string trimmedKey = key.Trim();

        if (string.IsNullOrWhiteSpace(trimmedKey))
            return Error.Validation(description: "Key is required.");

        string normalized = trimmedKey.ToUpperInvariant();

        RoleColourOption? option = await db.RoleColourOptions.SingleOrDefaultAsync(
            o => o.GuildId == guildId && o.NormalisedKey == normalized,
            ct);

        if (option is null)
            return Error.NotFound(description: $"Colour option `{trimmedKey}` is not registered.");

        List<MemberColourPreference> affected = await db
            .MemberColourPreferences.Where(p =>
                p.GuildId == guildId
                && p.Kind == MemberColourPreferenceKind.ConfiguredOption
                && p.SelectedOptionId == option.OptionId
            )
            .ToListAsync(ct);

        foreach (MemberColourPreference preference in affected)
            preference.SelectNoColour();

        db.RoleColourOptions.Remove(option);
        await db.SaveChangesAsync(ct);
        return option;
    }
}