using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Type Configuration for EventRider entity.
/// </summary>
/// <remarks>
/// EVENTRIDER TABLE STRUCTURE:
/// ===========================
/// THE MOST CRITICAL TABLE IN THE SYSTEM!
///
/// This table contains ALL event-specific rider data:
/// - Handicap (changes per event) - foundation of fantasy scoring
/// - All-Star status (determines if points are doubled)
/// - Qualifying results (used for ML predictions)
/// - Race results (finish position, adjusted position)
/// - Fantasy points (calculated scoring)
/// - Injury status, pick trends, and more
///
/// WHY THIS IS THE CORE TABLE:
/// ===========================
/// 1. **Fantasy Scoring**: Contains handicap and All-Star status
/// 2. **ML Predictions**: Provides most input features (handicap, qualifying, pick trend)
/// 3. **Results Tracking**: Stores finish position and calculated fantasy points
/// 4. **Team Building**: EventRiders are selected for fantasy teams
///
/// TYPICAL DATA VOLUME:
/// ====================
/// - 40-80 riders per event (20-40 per class)
/// - 17 Supercross + 12 Motocross + 3 SuperMotocross events = ~32 events/year
/// - Total: ~2,000 EventRider records per year
///
/// INDEXING STRATEGY:
/// ==================
/// - Unique composite index on (EventId, RiderId) - rider can't be in event twice
/// - Index on BikeClass for filtering 250 vs 450 riders
/// - Index on IsAllStar for All-Star requirement validation
/// - Composite index on (EventId, BikeClass) for class-specific queries
/// - Index on FinishPosition for leaderboard sorting
///
/// QUERY PATTERNS:
/// ===============
/// 1. Get all riders for an event:
///    WHERE event_id = @EventId
///
/// 2. Get available 250 riders (non-injured, with data):
///    WHERE event_id = @EventId AND bike_class = '250' AND is_injured = false
///
/// 3. Get All-Stars for constraint validation:
///    WHERE event_id = @EventId AND bike_class = @Class AND is_all_star = true
///
/// 4. ML training data (completed events with results):
///    WHERE finish_position IS NOT NULL AND fantasy_points IS NOT NULL
/// </remarks>
public class EventRiderConfiguration : IEntityTypeConfiguration<EventRider>
{
    public void Configure(EntityTypeBuilder<EventRider> builder)
    {
        // ==============================================================
        // TABLE AND PRIMARY KEY
        // ==============================================================

        builder.ToTable("event_riders");

        builder.HasKey(er => er.Id);

        // ==============================================================
        // COLUMNS - IDENTIFIERS
        // ==============================================================

        builder.Property(er => er.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(er => er.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(er => er.RiderId)
            .HasColumnName("rider_id")
            .IsRequired();

        // ==============================================================
        // COLUMNS - CLASSIFICATION
        // ==============================================================

        // BikeClass enum → PostgreSQL enum type
        builder.Property(er => er.BikeClass)
            .HasColumnName("bike_class")
            .HasConversion<string>()
            .IsRequired();

        // ==============================================================
        // COLUMNS - FANTASY SCORING INPUTS (most important!)
        // ==============================================================

        // Handicap: THE MOST IMPORTANT FIELD for fantasy scoring
        // Typical range: -5 to +10
        builder.Property(er => er.Handicap)
            .HasColumnName("handicap")
            .IsRequired();

        // All-Star: Determines if points are doubled
        // Critical business rule: All-Stars NEVER get doubled
        builder.Property(er => er.IsAllStar)
            .HasColumnName("is_all_star")
            .IsRequired();

        // Injury status: Filter out from recommendations
        builder.Property(er => er.IsInjured)
            .HasColumnName("is_injured")
            .IsRequired()
            .HasDefaultValue(false);

        // ==============================================================
        // COLUMNS - ML PREDICTION FEATURES
        // ==============================================================

        // Pick trend: % of players picking this rider (0-100)
        builder.Property(er => er.PickTrend)
            .HasColumnName("pick_trend")
            .HasPrecision(5, 2); // e.g., 72.50%

        // Qualifying position: Combined qualifying result (1 = fastest)
        builder.Property(er => er.CombinedQualyPosition)
            .HasColumnName("combined_qualy_position");

        // Best qualifying lap time in seconds
        builder.Property(er => er.BestQualyLapSeconds)
            .HasColumnName("best_qualy_lap_seconds")
            .HasPrecision(10, 3); // e.g., 48.327 seconds

        // Gap to fastest qualifier in seconds
        builder.Property(er => er.QualyGapToLeader)
            .HasColumnName("qualy_gap_to_leader")
            .HasPrecision(10, 3); // e.g., 1.234 seconds

        // ==============================================================
        // COLUMNS - RACE RESULTS
        // ==============================================================

        // Actual finish position (1 = winner, 22+ = DNF typically)
        // NULL until race completes
        builder.Property(er => er.FinishPosition)
            .HasColumnName("finish_position");

        // Finish position after handicap adjustment
        // Calculated: FinishPosition - Handicap (minimum 1)
        // NULL until race completes
        builder.Property(er => er.HandicapAdjustedPosition)
            .HasColumnName("handicap_adjusted_position");

        // Calculated fantasy points
        // NULL until race completes and CalculateFantasyPoints() is called
        builder.Property(er => er.FantasyPoints)
            .HasColumnName("fantasy_points");

        // ==============================================================
        // COLUMNS - TIMESTAMPS
        // ==============================================================

        builder.Property(er => er.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(er => er.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // ==============================================================
        // INDEXES
        // ==============================================================

        // Unique composite index on (EventId, RiderId)
        // Business rule: A rider can't be in the same event twice
        // Prevents duplicate EventRider records
        builder.HasIndex(er => new { er.EventId, er.RiderId })
            .IsUnique()
            .HasDatabaseName("ix_event_riders_event_rider_unique");

        // Index on BikeClass for filtering 250 vs 450 riders
        // Query pattern: WHERE bike_class = '250'
        builder.HasIndex(er => er.BikeClass)
            .HasDatabaseName("ix_event_riders_bike_class");

        // Index on IsAllStar for All-Star requirement validation
        // Query pattern: WHERE is_all_star = true
        builder.HasIndex(er => er.IsAllStar)
            .HasDatabaseName("ix_event_riders_is_all_star");

        // Composite index on (EventId, BikeClass) for class-specific event queries
        // Most common query: Get all 250 riders for an event
        // WHERE event_id = @EventId AND bike_class = '250'
        builder.HasIndex(er => new { er.EventId, er.BikeClass })
            .HasDatabaseName("ix_event_riders_event_class");

        // Composite index on (EventId, BikeClass, IsAllStar) for All-Star filtering
        // Query pattern: Find All-Stars in specific class for an event
        // WHERE event_id = @EventId AND bike_class = '450' AND is_all_star = true
        builder.HasIndex(er => new { er.EventId, er.BikeClass, er.IsAllStar })
            .HasDatabaseName("ix_event_riders_event_class_allstar");

        // Index on FinishPosition for leaderboard sorting
        // Query pattern: ORDER BY finish_position ASC
        builder.HasIndex(er => er.FinishPosition)
            .HasDatabaseName("ix_event_riders_finish_position");

        // Composite index on (EventId, FinishPosition) for event-specific results
        // Query pattern: Event results ordered by finish
        // WHERE event_id = @EventId ORDER BY finish_position
        builder.HasIndex(er => new { er.EventId, er.FinishPosition })
            .HasDatabaseName("ix_event_riders_event_finish");

        // ==============================================================
        // RELATIONSHIPS
        // ==============================================================

        // Many EventRiders -> One Event (participation belongs to one event)
        builder.HasOne(er => er.Event)
            .WithMany(e => e.EventRiders)
            .HasForeignKey(er => er.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // Many EventRiders -> One Rider (participations belong to one rider)
        builder.HasOne(er => er.Rider)
            .WithMany(r => r.EventRiders)
            .HasForeignKey(er => er.RiderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
