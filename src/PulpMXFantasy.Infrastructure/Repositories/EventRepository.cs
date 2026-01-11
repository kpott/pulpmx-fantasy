using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Domain.Entities;
using PulpMXFantasy.Infrastructure.Data;

namespace PulpMXFantasy.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Event entity queries.
/// </summary>
/// <remarks>
/// WHY THIS REPOSITORY EXISTS:
/// ===========================
/// Following Clean Architecture / Repository Pattern:
/// - Application layer defines IEventRepository interface
/// - Infrastructure layer provides this EF Core implementation
/// - Application layer event handlers can query events without EF Core dependency
///
/// QUERY OPTIMIZATION:
/// ===================
/// - Uses AsNoTracking for read-only queries (better performance)
/// - Includes navigation properties only when needed
/// - Filters at database level (not in memory)
///
/// THREAD SAFETY:
/// ==============
/// This repository should be registered as Scoped (one per request/message).
/// Each consumer gets its own DbContext and repository instance.
/// </remarks>
public class EventRepository : IEventRepository
{
    private readonly ApplicationDbContext _dbContext;

    /// <summary>
    /// Creates a new EventRepository instance.
    /// </summary>
    /// <param name="dbContext">Application database context</param>
    public EventRepository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Event?> GetNextUpcomingEventAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .Where(e => !e.IsCompleted && e.EventDate >= DateTimeOffset.UtcNow)
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Event?> GetEventWithRidersAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Events
            .AsNoTracking()
            .Include(e => e.EventRiders)
                .ThenInclude(er => er.Rider)
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<EventRider>> GetEventRidersWithDetailsAsync(
        Guid eventId,
        IEnumerable<Guid>? riderIds = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.EventRiders
            .AsNoTracking()
            .Include(er => er.Rider)
            .Where(er => er.EventId == eventId);

        if (riderIds != null)
        {
            var riderIdList = riderIds.ToList();
            if (riderIdList.Count > 0)
            {
                query = query.Where(er => riderIdList.Contains(er.RiderId));
            }
        }

        return await query.ToListAsync(cancellationToken);
    }
}
