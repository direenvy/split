using Microsoft.EntityFrameworkCore;
using Split.Api.Data;

namespace Split.Api;

/// <summary>Deletes anonymous groups that have had no activity for 90 days.</summary>
public sealed class AnonymousGroupCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<AnonymousGroupCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    /// <summary>Groups removed per DELETE, so the generated "IN (...)" stays a sane size.</summary>
    private const int BatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);

        do
        {
            try
            {
                await DeleteExpiredGroups(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Anonymous group cleanup failed; it will retry at the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DeleteExpiredGroups(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        SplitDbContext db = scope.ServiceProvider.GetRequiredService<SplitDbContext>();
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - RetentionPeriod;

        // Finding the groups and deleting them are two steps on purpose.
        //
        // The obvious one-liner — ExecuteDeleteAsync with the "nobody has this in their
        // history" test inlined — does not translate: ExecuteDelete cannot carry a subquery
        // over a second table, so SQLite threw on every run and the retention policy quietly
        // did nothing. As an ordinary query the same correlated EXISTS translates fine, so
        // the ids are read first and deleted by key afterwards.
        List<Guid> expired = await db.Groups
            .Where(group => group.LastActivityAt < cutoff
                && !db.GroupMemberships.Any(membership => membership.GroupId == group.Id))
            .Select(group => group.Id)
            .ToListAsync(cancellationToken);

        if (expired.Count == 0)
        {
            return;
        }

        int deleted = 0;
        foreach (Guid[] batch in expired.Chunk(BatchSize))
        {
            // Participants, expenses and shares go with the group: every child foreign key is
            // declared ON DELETE CASCADE and SQLite enforces it. That matters here because
            // ExecuteDelete issues raw SQL and never loads the children to cascade in memory.
            deleted += await db.Groups
                .Where(group => batch.Contains(group.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        logger.LogInformation("Removed {Count} anonymous groups inactive for at least 90 days.", deleted);
    }
}
