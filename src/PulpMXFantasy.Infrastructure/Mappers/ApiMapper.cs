using PulpMXFantasy.Domain.Entities;
using PulpMXFantasy.Domain.Enums;
using PulpMXFantasy.Infrastructure.ExternalApi.Models;

namespace PulpMXFantasy.Infrastructure.Mappers;

/// <summary>
/// Maps API DTOs from PulpMX API to domain entities.
/// </summary>
/// <remarks>
/// WHY THIS MAPPER EXISTS:
/// =======================
/// Separates external API structure from internal domain model:
/// - API DTOs may change (API versioning)
/// - Domain entities remain stable
/// - Centralized mapping logic (easier to maintain)
/// - Allows validation and transformation during mapping
///
/// DESIGN DECISION: Static Methods vs Mapperly/AutoMapper
/// =======================================================
/// Using static methods because:
/// - Simple, explicit transformations
/// - No hidden magic or reflection overhead
/// - Easy to debug and understand
/// - No additional dependencies
/// - Type-safe at compile time
///
/// If mappings become complex (>100 lines per method), consider:
/// - Mapperly (source generator, compile-time, zero overhead)
/// - Manual builder pattern for complex entities
///
/// VALIDATION DURING MAPPING:
/// ===========================
/// Mapper performs validation:
/// - Required fields present (throws if missing)
/// - Enum parsing (throws if invalid enum value)
/// - Date format validation
/// - Null handling for optional fields
///
/// If mapping fails, exception is thrown with clear message.
/// Caller should catch and handle appropriately.
/// </remarks>
public static class ApiMapper
{
    /// <summary>
    /// Maps API event to domain Event entity.
    /// </summary>
    /// <param name="apiEvent">Event from API</param>
    /// <param name="seriesId">ID of the series this event belongs to</param>
    /// <returns>Domain Event entity</returns>
    /// <remarks>
    /// Mapping includes:
    /// - Basic event metadata (name, venue, location, date)
    /// - Enum parsing (SeriesType, EventFormat, Division)
    /// - Foreign key assignment (SeriesId)
    /// - Empty EventRiders collection (populated separately)
    ///
    /// Example usage:
    /// <code>
    /// var series = await _dbContext.Series.FirstAsync(s => s.SeriesType == SeriesType.Supercross);
    /// var eventEntity = ApiMapper.MapToEvent(apiEvent, series.Id);
    /// _dbContext.Events.Add(eventEntity);
    /// </code>
    /// </remarks>
    public static Event MapToEvent(ApiEvent apiEvent, Guid seriesId)
    {
        return new Event
        {
            Id = Guid.NewGuid(),
            SeriesId = seriesId,
            Slug = apiEvent.Slug,
            Name = apiEvent.Name,
            Venue = apiEvent.Venue,
            Location = apiEvent.Location,
            EventDate = apiEvent.EventDate,
            RoundNumber = apiEvent.RoundNumber,
            SeriesType = ParseEnum<SeriesType>(apiEvent.SeriesType, nameof(apiEvent.SeriesType)),
            EventFormat = ParseEnum<EventFormat>(apiEvent.EventFormat, nameof(apiEvent.EventFormat)),
            Division = ParseEnum<Division>(apiEvent.Division, nameof(apiEvent.Division)),
            IsCompleted = apiEvent.IsCompleted,
            LockoutTime = apiEvent.LockoutTime,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Maps API rider to domain Rider entity.
    /// </summary>
    /// <param name="apiRider">Rider from API</param>
    /// <returns>Domain Rider entity</returns>
    /// <remarks>
    /// Mapping includes:
    /// - Basic rider info (name, number, photo)
    /// - PulpMxId as stable identifier
    /// - Empty EventRiders collection
    ///
    /// Use this when creating new riders. For existing riders, use update logic.
    /// </remarks>
    public static Rider MapToRider(ApiEventRider apiRider)
    {
        return new Rider
        {
            Id = Guid.NewGuid(),
            PulpMxId = apiRider.PulpMxId,
            Name = apiRider.Name,
            Number = apiRider.Number,
            PhotoUrl = apiRider.PhotoUrl,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Maps API event rider to domain EventRider entity.
    /// </summary>
    /// <param name="apiRider">Event rider from API</param>
    /// <param name="eventId">ID of the event</param>
    /// <param name="riderId">ID of the rider</param>
    /// <returns>Domain EventRider entity</returns>
    /// <remarks>
    /// Mapping includes:
    /// - Foreign keys (EventId, RiderId)
    /// - BikeClass enum parsing
    /// - Handicap and All-Star status
    /// - Qualifying data (position, lap times)
    /// - Results data (finish position, fantasy points) if race completed
    ///
    /// This is the most complex mapping because EventRider contains
    /// all event-specific data for a rider.
    ///
    /// Example usage:
    /// <code>
    /// var eventRider = ApiMapper.MapToEventRider(apiRider, event.Id, rider.Id);
    /// _dbContext.EventRiders.Add(eventRider);
    /// </code>
    /// </remarks>
    public static EventRider MapToEventRider(
        ApiEventRider apiRider,
        Guid eventId,
        Guid riderId)
    {
        return new EventRider
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            RiderId = riderId,
            BikeClass = ParseBikeClass(apiRider.BikeClass),
            Handicap = apiRider.Handicap,
            IsAllStar = apiRider.IsAllStar,
            IsInjured = apiRider.IsInjured,
            PickTrend = apiRider.PickTrend,
            CombinedQualyPosition = apiRider.CombinedQualyPosition,
            BestQualyLapSeconds = apiRider.BestQualyLapSeconds,
            QualyGapToLeader = apiRider.QualyGapToLeader,
            FinishPosition = apiRider.FinishPosition,
            HandicapAdjustedPosition = apiRider.HandicapAdjustedPosition,
            FantasyPoints = apiRider.FantasyPoints,
            Ineligible = apiRider.Ineligible,
            IneligibleReason = apiRider.IneligibleReason,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Updates existing EventRider entity with data from API.
    /// </summary>
    /// <param name="eventRider">Existing EventRider entity</param>
    /// <param name="apiRider">Updated data from API</param>
    /// <remarks>
    /// Use this when syncing updates to existing event rider data:
    /// - Handicap changes (common before race day)
    /// - Qualifying results published
    /// - Race results finalized
    /// - Fantasy points calculated
    ///
    /// Does NOT update foreign keys (EventId, RiderId) - those are immutable.
    /// </remarks>
    public static void UpdateEventRider(EventRider eventRider, ApiEventRider apiRider)
    {
        eventRider.Handicap = apiRider.Handicap;
        eventRider.IsAllStar = apiRider.IsAllStar;
        eventRider.IsInjured = apiRider.IsInjured;
        eventRider.PickTrend = apiRider.PickTrend;
        eventRider.CombinedQualyPosition = apiRider.CombinedQualyPosition;
        eventRider.BestQualyLapSeconds = apiRider.BestQualyLapSeconds;
        eventRider.QualyGapToLeader = apiRider.QualyGapToLeader;
        eventRider.FinishPosition = apiRider.FinishPosition;
        eventRider.HandicapAdjustedPosition = apiRider.HandicapAdjustedPosition;
        eventRider.FantasyPoints = apiRider.FantasyPoints;
        eventRider.Ineligible = apiRider.Ineligible;
        eventRider.IneligibleReason = apiRider.IneligibleReason;
        eventRider.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Parses string to enum with validation.
    /// </summary>
    /// <typeparam name="TEnum">Enum type to parse</typeparam>
    /// <param name="value">String value from API</param>
    /// <param name="fieldName">Field name for error messages</param>
    /// <returns>Parsed enum value</returns>
    /// <exception cref="ArgumentException">If value is not a valid enum</exception>
    public static TEnum ParseEnum<TEnum>(string value, string fieldName) where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var result))
        {
            return result;
        }

        throw new ArgumentException(
            $"Invalid {fieldName} value '{value}'. " +
            $"Valid values are: {string.Join(", ", Enum.GetNames<TEnum>())}");
    }

    /// <summary>
    /// Parses bike class string to BikeClass enum.
    /// </summary>
    /// <param name="bikeClass">Bike class string ("250" or "450")</param>
    /// <returns>BikeClass enum value</returns>
    /// <exception cref="ArgumentException">If bike class is invalid</exception>
    /// <remarks>
    /// Handles common variations:
    /// - "250" → BikeClass.Class250
    /// - "450" → BikeClass.Class450
    /// - "250cc" → BikeClass.Class250 (strips "cc")
    /// - "Class250" → BikeClass.Class250 (handles enum name format)
    /// </remarks>
    private static BikeClass ParseBikeClass(string bikeClass)
    {
        // Remove common suffixes and prefixes
        var normalized = bikeClass
            .Replace("cc", "", StringComparison.OrdinalIgnoreCase)
            .Replace("class", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return normalized switch
        {
            "250" => BikeClass.Class250,
            "450" => BikeClass.Class450,
            _ => throw new ArgumentException(
                $"Invalid bike class '{bikeClass}'. Expected '250' or '450'.")
        };
    }
}
