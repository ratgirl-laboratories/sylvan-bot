using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourReconciliationWorker(
    DiscordSocketClient discordClient,
    IDbContextFactory<BotDbContext> dbContextFactory,
    RoleColourReconciler reconciler,
    ILogger logger
) : BackgroundService
{
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(30);
    private readonly ILogger _logger = logger.ForContext<RoleColourReconciliationWorker>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(ReconciliationInterval);

        do
        {
            await ReconcileConfiguredGuildsAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task ReconcileConfiguredGuildsAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await using BotDbContext db = await dbContextFactory.CreateDbContextAsync(ct);

            ulong[] configuredGuildIds = await db.RoleColourOptions.AsNoTracking().Select(option => option.GuildId).Distinct().ToArrayAsync(ct);

            foreach (ulong guildId in configuredGuildIds)
            {
                ct.ThrowIfCancellationRequested();
                SocketGuild? guild = discordClient.GetGuild(guildId);

                if (guild is null)
                {
                    _logger.Warning("Role colour reconciliation skipped because guild {GuildId} is unavailable.", guildId);

                    continue;
                }

                try
                {
                    int changed = await reconciler.ReconcileGuildAsync(guild, ct);

                    _logger.Information(
                        "Role colour reconciliation completed for guild {GuildId}; changed {ChangedCount} members.",
                        guildId,
                        changed
                    );
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Role colour reconciliation failed for guild {GuildId}; retrying next interval.", guildId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Role colour reconciliation scan failed; retrying next interval.");
        }
    }
}
