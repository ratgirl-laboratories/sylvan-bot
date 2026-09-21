using RatBot.BackgroundWorkers;
using Serilog;
using Shouldly;

namespace RatBot.Discord.Tests.BackgroundWorkers;

[TestFixture]
public sealed class MultiGuildBackgroundWorkerTests
{
    [Test]
    public async Task GuildMemberCacheWorker_VisitsEveryGuildAndContinuesAfterFailure()
    {
        List<ulong> visited = [];

        await GuildMemberCacheBackgroundWorker.ProcessGuildsAsync(
            [1UL, 2UL, 3UL],
            guildId => guildId,
            guildId =>
            {
                visited.Add(guildId);

                if (guildId == 2)
                    throw new InvalidOperationException("boom");

                return Task.CompletedTask;
            },
            Log.Logger,
            CancellationToken.None
        );

        visited.ShouldBe([1UL, 2UL, 3UL]);
    }
}
