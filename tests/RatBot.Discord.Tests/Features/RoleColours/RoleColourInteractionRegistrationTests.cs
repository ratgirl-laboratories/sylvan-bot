using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;
using Serilog;
using Shouldly;

namespace RatBot.Discord.Tests.Features.RoleColours;

[TestFixture]
public sealed class RoleColourInteractionRegistrationTests
{
    [Test]
    public async Task InteractionService_RegistersExistingCommandsAndComponentIds()
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddDbContextFactory<BotDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()))
            .AddSingleton<ILogger>(Log.Logger)
            .AddScoped<RoleColourOperations>()
            .AddSingleton<RoleColourReconciler>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using DiscordSocketClient client = new DiscordSocketClient();

        using InteractionService interactions = new InteractionService(client, new InteractionServiceConfig { AutoServiceScopes = true });

        ModuleInfo colour = await interactions.AddModuleAsync<ColourModule>(services);
        ModuleInfo admin = await interactions.AddModuleAsync<RoleColourAdminModule>(services);

        colour.SlashGroupName.ShouldBe("colour");
        colour.SlashCommands.Select(command => command.Name).ShouldBe(["swap", "remove"], ignoreOrder: true);

        colour.ComponentCommands.Select(command => command.Name).ShouldBe(["colour-swap:apply:*:*", "colour-swap:select:*"], ignoreOrder: true);

        admin.SlashGroupName.ShouldBe("colour-admin");

        admin.SlashCommands.Select(command => command.Name).ShouldBe(["add", "upsert", "delete", "list", "sync"], ignoreOrder: true);
    }
}
