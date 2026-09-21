using Discord.WebSocket;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RatBot.Features.RoleColours;
using RatBot.Infrastructure.Data;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace RatBot.Discord.Tests.Features.RoleColours;

[TestFixture]
public sealed class RoleColourReconciliationWorkerTests
{
    [Test]
    public async Task Scan_LogsDatabaseFailureAndCanRecoverOnNextScan()
    {
        DbContextOptions<BotDbContext> options = CreateOptions();
        await SeedAsync(options, 1);
        IDbContextFactory<BotDbContext> factory = Substitute.For<IDbContextFactory<BotDbContext>>();

        factory
            .CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new BotDbContext(new DbContextOptions<BotDbContext>())), _ => Task.FromResult(new BotDbContext(options)));

        RecordingSink sink = new RecordingSink();
        await using Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        await using DiscordSocketClient client = new DiscordSocketClient();

        using RoleColourReconciliationWorker worker = new RoleColourReconciliationWorker(
            client,
            factory,
            new RoleColourReconciler(factory, logger),
            logger
        );

        await worker.ReconcileConfiguredGuildsAsync(CancellationToken.None);

        LogEvent failure = sink.Events.ShouldHaveSingleItem();
        failure.Level.ShouldBe(LogEventLevel.Error);
        failure.Exception.ShouldBeOfType<InvalidOperationException>();
        failure.RenderMessage().ShouldBe("Role colour reconciliation scan failed; retrying next interval.");

        await worker.ReconcileConfiguredGuildsAsync(CancellationToken.None);

        sink.Events.Count.ShouldBe(2);
        sink.Events[1].RenderMessage().ShouldBe("Role colour reconciliation skipped because guild 1 is unavailable.");
    }

    [Test]
    public async Task Scan_VisitsEachConfiguredGuildOnceAndSkipsUnavailableGuilds()
    {
        DbContextOptions<BotDbContext> options = CreateOptions();
        await SeedAsync(options, 1, 2, 1);
        IDbContextFactory<BotDbContext> factory = Substitute.For<IDbContextFactory<BotDbContext>>();

        factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(new BotDbContext(options)));

        RecordingSink sink = new RecordingSink();
        await using Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        await using DiscordSocketClient client = new DiscordSocketClient();

        using RoleColourReconciliationWorker worker = new RoleColourReconciliationWorker(
            client,
            factory,
            new RoleColourReconciler(factory, logger),
            logger
        );

        await worker.ReconcileConfiguredGuildsAsync(CancellationToken.None);

        sink.Events.Select(entry => entry.RenderMessage())
            .ShouldBe(
                new[]
                {
                    "Role colour reconciliation skipped because guild 1 is unavailable.",
                    "Role colour reconciliation skipped because guild 2 is unavailable.",
                },
                ignoreOrder: true
            );

        await factory.Received(1).CreateDbContextAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Scan_PropagatesCancellationWithoutLoggingFailure()
    {
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        IDbContextFactory<BotDbContext> factory = Substitute.For<IDbContextFactory<BotDbContext>>();

        factory
            .CreateDbContextAsync(cancellation.Token)
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<BotDbContext>(cancellation.Token);
            });

        RecordingSink sink = new RecordingSink();
        await using Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        await using DiscordSocketClient client = new DiscordSocketClient();

        using RoleColourReconciliationWorker worker = new RoleColourReconciliationWorker(
            client,
            factory,
            new RoleColourReconciler(factory, logger),
            logger
        );

        await Should.ThrowAsync<OperationCanceledException>(() => worker.ReconcileConfiguredGuildsAsync(cancellation.Token));

        sink.Events.ShouldBeEmpty();
    }

    private static DbContextOptions<BotDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<BotDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static async Task SeedAsync(DbContextOptions<BotDbContext> options, params ulong[] guildIds)
    {
        await using BotDbContext db = new BotDbContext(options);

        for (int i = 0; i < guildIds.Length; i++)
            db.RoleColourOptions.Add(RoleColourOption.Create(guildIds[i], $"colour-{i}", $"Colour {i}", (ulong)i + 10, (ulong)i + 20).Value);

        await db.SaveChangesAsync();
    }

    private sealed class RecordingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
