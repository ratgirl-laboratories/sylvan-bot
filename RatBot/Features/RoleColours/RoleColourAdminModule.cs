using System.Text;

namespace RatBot.Features.RoleColours;

[Group("colour-admin", "Administrative role colour commands.")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
public sealed class RoleColourAdminModule(RoleColourOperations operations, RoleColourReconciler reconciler)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string ResponseNoGuild = "This command can only be used in a guild.";

    [SlashCommand("add", "Register a source/display role colour mapping.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task AddAsync(
        [Summary("name", "Name used to identify this colour.")]
        string name,
        [Summary("source", "Source colour role users select.")]
        IRole source,
        [Summary("display", "Display colour role RatBot manages.")]
        IRole display
    )
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        ErrorOr<Success> roleValidation = ValidateRoleColourRoles(source.Id, display.Id);

        if (roleValidation.IsError)
        {
            await RespondAsync(roleValidation.FirstError.Description, ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        ErrorOr<RoleColourOption> result = await operations.AddMappingAsync(
            Context.Guild.Id,
            name,
            name,
            source.Id,
            display.Id,
            CancellationToken.None
        );

        await result.SwitchFirstAsync(
            async option =>
            {
                await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None);

                await FollowupAsync(
                    $"Registered colour option `{option.Key}` (‘{option.Label}’): {source.Mention} -> {display.Mention}.",
                    ephemeral: true
                );
            },
            async error => await FollowupAsync(error.Description, ephemeral: true)
        );
    }

    [SlashCommand("upsert", "Create or update a source/display role colour mapping.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task UpsertAsync(
        [Summary("name", "Name used to identify this colour.")]
        string name,
        [Summary("source", "Source colour role users select.")]
        IRole source,
        [Summary("display", "Display colour role RatBot manages.")]
        IRole display
    )
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        ErrorOr<Success> roleValidation = ValidateRoleColourRoles(source.Id, display.Id);

        if (roleValidation.IsError)
        {
            await RespondAsync(roleValidation.FirstError.Description, ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        ErrorOr<(RoleColourOption Option, bool Created)> result = await operations.UpsertMappingAsync(
            Context.Guild.Id,
            name,
            name,
            source.Id,
            display.Id,
            CancellationToken.None
        );

        await result.SwitchFirstAsync(
            async upsert =>
            {
                await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None);
                string action = upsert.Created ? "Registered" : "Updated";

                await FollowupAsync(
                    $"{action} colour option `{upsert.Option.Key}` (‘{upsert.Option.Label}’): {source.Mention} -> {display.Mention}.",
                    ephemeral: true
                );
            },
            async error => await FollowupAsync(error.Description, ephemeral: true)
        );
    }

    [SlashCommand("delete", "Delete a configured role colour option.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task DeleteAsync([Summary("name", "Name/key of the colour option to delete.")] string name)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        ErrorOr<RoleColourOption> result = await operations.DeleteMappingAsync(
            Context.Guild.Id,
            name,
            CancellationToken.None);

        await result.SwitchFirstAsync(
            async option =>
            {
                await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None);
                await FollowupAsync($"Deleted colour option `{option.Key}`.", ephemeral: true);
            },
            async error => await FollowupAsync(error.Description, ephemeral: true)
        );
    }

    [SlashCommand("list", "List configured role colour options.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ListAsync(
        [Summary("include-disabled", "Include disabled colour options.")] bool includeDisabled = true)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        ImmutableArray<RoleColourOption> options = await operations.ListConfiguredOptionsAsync(
            Context.Guild.Id,
            includeDisabled,
            CancellationToken.None
        );

        if (options.IsEmpty)
        {
            await RespondAsync("No colour options are configured.", ephemeral: true);
            return;
        }

        await RespondAsync(BuildListResponse(options), ephemeral: true);
    }

    [SlashCommand("sync", "Reconcile display colour roles for all members who currently have a source colour role.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task SyncAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync(ResponseNoGuild, ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        ImmutableArray<RoleColourOption> enabled = await operations.ListConfiguredOptionsAsync(
            Context.Guild.Id,
            false,
            CancellationToken.None
        );

        if (enabled.IsEmpty)
        {
            await FollowupAsync("No enabled colour options are configured.", ephemeral: true);
            return;
        }

        int changed = await reconciler.ReconcileGuildAsync(Context.Guild, CancellationToken.None);
        await FollowupAsync($"Reconciled display colour roles for {changed} member(s).", ephemeral: true);
    }

    private ErrorOr<Success> ValidateRoleColourRoles(ulong sourceRoleId, ulong displayRoleId)
    {
        SocketRole? sourceRole = Context.Guild.GetRole(sourceRoleId);
        SocketRole? displayRole = Context.Guild.GetRole(displayRoleId);

        if (sourceRole is null)
            return Error.Validation(description: "Source role does not exist in this guild.");

        if (displayRole is null)
            return Error.Validation(description: "Display role does not exist in this guild.");

        if (sourceRole.Colors.PrimaryColor != Color.Default)
            return Error.Validation(description: "Source role must not have a colour set. Clear the colour first.");

        return Result.Success;
    }

    private static string BuildListResponse(IReadOnlyList<RoleColourOption> options)
    {
        StringBuilder builder = new StringBuilder("Configured colour options:");

        foreach (RoleColourOption option in options)
        {
            string state = option.IsEnabled ? "enabled" : "disabled";

            builder.AppendLine()
                .Append($"`{option.Key}`: <@&{option.SourceRoleId}> -> <@&{option.DisplayRoleId}> ({state})");
        }

        return builder.ToString();
    }
}