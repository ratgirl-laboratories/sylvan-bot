using RatBot.Gateway;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourGatewayHandler(DiscordSocketClient discordClient, RoleColourReconciler reconciler, ILogger logger)
    : IDiscordGatewayHandler
{
    private readonly ILogger _logger = logger.ForContext<RoleColourGatewayHandler>();

    public Task InitializeAsync(CancellationToken ct)
    {
        Subscribe();
        return Task.CompletedTask;
    }

    public void Unsubscribe() => discordClient.GuildMemberUpdated -= HandleGuildMemberUpdated;

    private async Task HandleGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        IReadOnlyCollection<ulong>? beforeRoles = before.HasValue ? ((IGuildUser)before.Value).RoleIds : null;
        IReadOnlyCollection<ulong> afterRoles = ((IGuildUser)after).RoleIds;
        bool rolesChanged =
            beforeRoles is null
            || beforeRoles.Count != afterRoles.Count
            || !beforeRoles.OrderBy(roleId => roleId).SequenceEqual(afterRoles.OrderBy(roleId => roleId));

        if (!rolesChanged)
            return;

        _logger
            .ForContext("GuildId", after.Guild.Id)
            .ForContext("UserId", after.Id)
            .ForContext("BeforeRoleCount", beforeRoles?.Count)
            .ForContext("AfterRoleCount", afterRoles.Count)
            .Debug("Guild member roles changed; reconciling role colour.");

        await reconciler.ReconcileMemberAsync(after.Guild, after.Id, CancellationToken.None);
    }

    private void Subscribe() => discordClient.GuildMemberUpdated += HandleGuildMemberUpdated;
}
