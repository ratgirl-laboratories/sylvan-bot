namespace RatBot.Features.RoleColours;

[Group("colour", "Pick or remove your display colour.")]
public sealed class ColourModule(RoleColourOperations operations, RoleColourReconciler reconciler)
    : InteractionModuleBase<IInteractionContext>
{
    private const string SwapPrefix = "colour-swap";

    [ComponentInteraction($"{SwapPrefix}:apply:*:*", true)]
    public async Task OnSwapApplyAsync(ulong ownerUserId, string optionId)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);
            return;
        }

        if (Context.User.Id != ownerUserId)
        {
            await RespondAsync("This colour selection menu is not for you.", ephemeral: true);
            return;
        }

        if (!Guid.TryParse(optionId, out Guid selectedOptionId))
        {
            await DisableComponentsAsync();
            await RespondAsync("Invalid selection.", ephemeral: true);
            return;
        }

        // Revalidate eligibility against current roles and option state
        IGuildUser invoker = (IGuildUser)Context.User;
        IReadOnlyCollection<ulong> roleIds = invoker.RoleIds;

        ErrorOr<RoleColourOption> result = await operations.SelectOptionAsync(
            Context.Guild.Id,
            Context.User.Id,
            new RoleColourOption.Id(selectedOptionId),
            roleIds,
            CancellationToken.None
        );

        if (result.IsError)
        {
            await DisableComponentsAsync();
            await RespondAsync("That colour is no longer available to you.", ephemeral: true);
            return;
        }

        Log.Debug(
            "colour_swap apply_ok guild_id={GuildId} user_id={UserId} option_id={OptionId}",
            Context.Guild.Id,
            Context.User.Id,
            selectedOptionId
        );

        await reconciler.ReconcileMemberAsync((SocketGuild)Context.Guild, Context.User.Id, CancellationToken.None);
        Log.Debug("colour_swap reconciled guild_id={GuildId} user_id={UserId}", Context.Guild.Id, Context.User.Id);

        string label = result.Value.Label;

        if (Context.Interaction is SocketMessageComponent smc2)
            await smc2.UpdateAsync(m =>
            {
                m.Content = $"You are now wearing {label}.";
                m.Components = new ComponentBuilder().Build();
            });
        else
            await RespondAsync($"You are now wearing {label}.", ephemeral: true);
    }

    [ComponentInteraction($"{SwapPrefix}:select:*", true)]
    public async Task OnSwapSelectAsync(ulong ownerUserId, string[] values)
    {
        if (Context.User.Id != ownerUserId)
        {
            await RespondAsync("This colour selection menu is not for you.", ephemeral: true);
            return;
        }

        if (values.Length == 0)
        {
            await RespondAsync("Please choose a colour.", ephemeral: true);
            return;
        }

        if (!Guid.TryParse(values[0], out Guid optId))
        {
            await RespondAsync("Invalid selection.", ephemeral: true);
            return;
        }

        Log.Debug(
            "colour_swap select guild_id={GuildId} user_id={UserId} option_id={OptionId}",
            Context.Guild?.Id,
            Context.User.Id,
            optId);

        // Rebuild components from fresh eligible list and mark selected as default
        IGuildUser invoker = (IGuildUser)Context.User;
        IReadOnlyCollection<ulong> roleIds = invoker.RoleIds;

        ImmutableArray<RoleColourOption> eligible = await operations.ListEligibleOptionsAsync(
            Context.Guild.Id,
            roleIds,
            CancellationToken.None
        );

        string applyId = $"{SwapPrefix}:apply:{ownerUserId}:{optId}";
        string selectId = $"{SwapPrefix}:select:{ownerUserId}";

        SelectMenuBuilder menu = new SelectMenuBuilder().WithCustomId(selectId)
            .WithPlaceholder("Choose a colour…")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (RoleColourOption opt in eligible)
        {
            bool isDefault = opt.OptionId.Value == optId;
            menu.AddOption(new SelectMenuOptionBuilder(opt.Label, opt.OptionId.Value.ToString(), isDefault: isDefault));
        }

        ComponentBuilder builder = new ComponentBuilder().WithSelectMenu(menu).WithButton("Apply", applyId);

        if (Context.Interaction is SocketMessageComponent smc)
            await smc.UpdateAsync(m => { m.Components = builder.Build(); });
        else
            // Fallback: acknowledge with an ephemeral response if somehow not a component
            await RespondAsync("Selection updated.", ephemeral: true, components: builder.Build());
    }

    [SlashCommand("remove", "Remove your display colour.")]
    public async Task RemoveAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);
            return;
        }

        Log.Debug("colour_remove start guild_id={GuildId} user_id={UserId}", Context.Guild.Id, Context.User.Id);
        await DeferAsync(ephemeral: true);

        await operations.SelectNoColourAsync(Context.Guild.Id, Context.User.Id, CancellationToken.None);

        Log.Debug("colour_remove reconcile guild_id={GuildId} user_id={UserId}", Context.Guild.Id, Context.User.Id);
        await reconciler.ReconcileMemberAsync((SocketGuild)Context.Guild, Context.User.Id, CancellationToken.None);
        await FollowupAsync("Your display colour has been removed.", ephemeral: true);
    }

    [SlashCommand("swap", "Swap to another available display colour.")]
    public async Task SwapAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command can only be used in a guild.", ephemeral: true);
            return;
        }

        IGuildUser invoker = (IGuildUser)Context.User;
        IReadOnlyCollection<ulong> roleIds = invoker.RoleIds;

        ImmutableArray<RoleColourOption> eligible = await operations.ListEligibleOptionsAsync(
            Context.Guild.Id,
            roleIds,
            CancellationToken.None
        );

        Log.Debug(
            "colour_swap start guild_id={GuildId} user_id={UserId} eligible_count={Eligible}",
            Context.Guild.Id,
            Context.User.Id,
            eligible.Length
        );

        switch (eligible.Length)
        {
            case 0:
                await RespondAsync("You do not currently have any colour roles that can be selected.", ephemeral: true);

                return;
            case > 25:
                await RespondAsync(
                    "You have too many eligible colours to show in one menu. Somehow. Uh, contact ratgirl I guess",
                    ephemeral: true);

                return;
        }

        string selectId = $"{SwapPrefix}:select:{Context.User.Id}";
        string applyId = $"{SwapPrefix}:apply:{Context.User.Id}:{Guid.Empty}";

        SelectMenuBuilder menu = new SelectMenuBuilder().WithCustomId(selectId).WithPlaceholder("Choose a colour…");

        foreach (RoleColourOption opt in eligible)
            menu.AddOption(opt.Label, opt.OptionId.Value.ToString());

        ComponentBuilder components =
            new ComponentBuilder().WithSelectMenu(menu).WithButton("Apply", applyId, disabled: true);

        await RespondAsync("Select a colour, then press Apply.", components: components.Build(), ephemeral: true);
    }

    private async Task DisableComponentsAsync()
    {
        try
        {
            if (Context.Interaction is SocketMessageComponent smc)
                await smc.UpdateAsync(m => { m.Components = new ComponentBuilder().Build(); });
        }
        catch
        {
            // Don't care
        }
    }
}