using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Type Configuration for Event entity.
/// </summary>
/// <remarks>
/// EVENT TABLE STRUCTURE:
/// ======================
/// Configures the race events table - one row per race event:
/// - Event identification (Slug, Name, Venue, Location)
/// - Scheduling (EventDate, RoundNumber)
/// - Series linkage (SeriesId, SeriesType)
/// - Format configuration (EventFormat, Division) - CRITICAL!
/// - Status (IsCompleted)
/// - Relationships (many EventRiders, belongs to Series)
///
/// CRITICAL FIELDS:
/// ================
/// 1. **EventFormat** (Standard, TripleCrown, Motocross, SuperMotocross)
///    - System FAILS without this - ~40% of events are non-standard
///    - Determines scoring logic branching
///    - Example: TripleCrown needs 3 race results, Motocross needs 2 moto results
///
/// 2. **Division** (East, West, Combined)
///    - CRITICAL for 250 class rider availability filtering
///    - Example: West event only shows West + Combined riders
///    - Database queries MUST filter: WHERE division = @EventDivision OR division = 'Combined'
///
/// 3. **Slug** - Unique API identifier (e.g., "anaheim-1-2025")
///    - Used to call PulpMX API endpoints
///    - Must be unique across all events
///
/// INDEXING STRATEGY:
/// ==================
/// - Unique index on Slug for API lookups
/// - Index on EventDate for "next event" and chronological queries
/// - Index on IsCompleted for filtering upcoming vs completed events
/// - Composite index on (SeriesId, RoundNumber) for consecutive pick validation
/// </remarks>
public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        // ==============================================================
        // TABLE AND PRIMARY KEY
        // ==============================================================

        builder.ToTable("events");

        builder.HasKey(e => e.Id);

        // ==============================================================
        // COLUMNS
        // ==============================================================

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(e => e.SeriesId)
            .HasColumnName("series_id")
            .IsRequired();

        builder.Property(e => e.Slug)
            .HasColumnName("slug")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Venue)
            .HasColumnName("venue")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Location)
            .HasColumnName("location")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.EventDate)
            .HasColumnName("event_date")
            .IsRequired();

        builder.Property(e => e.RoundNumber)
            .HasColumnName("round_number")
            .IsRequired();

        // SeriesType enum → PostgreSQL enum type
        builder.Property(e => e.SeriesType)
            .HasColumnName("series_type")
            .HasConversion<string>()
            .IsRequired();

        // EventFormat enum → PostgreSQL enum type
        // CRITICAL: Determines scoring logic (Standard, TripleCrown, Motocross, SuperMotocross)
        builder.Property(e => e.EventFormat)
            .HasColumnName("event_format")
            .HasConversion<string>()
            .IsRequired();

        // Division enum → PostgreSQL enum type
        // CRITICAL: Determines 250 class rider availability (East, West, Combined)
        builder.Property(e => e.Division)
            .HasColumnName("division")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.IsCompleted)
            .HasColumnName("is_completed")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // ==============================================================
        // INDEXES
        // ==============================================================

        // Unique index on Slug for API synchronization lookups
        // Example query: WHERE slug = 'anaheim-1-2025'
        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("ix_events_slug");

        // Index on EventDate for chronological queries and "next event" lookup
        // Most common queries: ORDER BY event_date or WHERE event_date >= NOW()
        builder.HasIndex(e => e.EventDate)
            .HasDatabaseName("ix_events_event_date");

        // Index on IsCompleted for filtering upcoming vs completed events
        // Used to: Generate predictions (false), train ML models (true)
        builder.HasIndex(e => e.IsCompleted)
            .HasDatabaseName("ix_events_is_completed");

        // Composite index on (SeriesId, RoundNumber) for consecutive pick validation
        // Query pattern: Find rider picks in previous round of same series
        // WHERE series_id = @SeriesId AND round_number = @RoundNumber - 1
        builder.HasIndex(e => new { e.SeriesId, e.RoundNumber })
            .HasDatabaseName("ix_events_series_round");

        // Composite index on (EventDate, IsCompleted) for "next upcoming event" queries
        // Most common query: WHERE is_completed = false AND event_date >= NOW() ORDER BY event_date LIMIT 1
        builder.HasIndex(e => new { e.EventDate, e.IsCompleted })
            .HasDatabaseName("ix_events_date_completed");

        // ==============================================================
        // RELATIONSHIPS
        // ==============================================================

        // Many Events -> One Series (event belongs to a series)
        builder.HasOne(e => e.Series)
            .WithMany(s => s.Events)
            .HasForeignKey(e => e.SeriesId)
            .OnDelete(DeleteBehavior.Cascade);

        // One Event -> Many EventRiders (event has many rider participations)
        builder.HasMany(e => e.EventRiders)
            .WithOne(er => er.Event)
            .HasForeignKey(er => er.EventId)
            .OnDelete(DeleteBehavior.Cascade); // If event deleted, delete all rider participations
    }
}
