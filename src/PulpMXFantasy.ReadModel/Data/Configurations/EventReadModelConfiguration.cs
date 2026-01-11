using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Data.Configurations;

/// <summary>
/// Entity Type Configuration for EventReadModel.
/// </summary>
/// <remarks>
/// EVENTS TABLE STRUCTURE:
/// =======================
/// Denormalized event data for UI display:
/// - Event identification (Id, Name, Slug)
/// - Location (Venue, Location)
/// - Timing (EventDate, IsCompleted)
/// - Series (SeriesName, SeasonYear)
/// - Metadata (RiderCount, SyncedAt)
///
/// POPULATED BY:
/// =============
/// Worker service populates this table when:
/// 1. Events are synced from PulpMX API
/// 2. Event data is updated
///
/// WEB ACCESS:
/// ===========
/// Web project queries this table for:
/// - Next upcoming event display
/// - Event details on predictions page
/// - Event history/archive views
/// </remarks>
public class EventReadModelConfiguration : IEntityTypeConfiguration<EventReadModel>
{
    public void Configure(EntityTypeBuilder<EventReadModel> builder)
    {
        builder.ToTable("events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Slug)
            .HasColumnName("slug")
            .HasMaxLength(100)
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

        builder.Property(e => e.SeriesName)
            .HasColumnName("series_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.SeasonYear)
            .HasColumnName("season_year")
            .IsRequired();

        builder.Property(e => e.IsCompleted)
            .HasColumnName("is_completed")
            .IsRequired();

        builder.Property(e => e.RiderCount)
            .HasColumnName("rider_count")
            .IsRequired();

        builder.Property(e => e.SyncedAt)
            .HasColumnName("synced_at")
            .IsRequired();

        // Indexes
        builder.HasIndex(e => e.Slug)
            .IsUnique()
            .HasDatabaseName("uq_events_slug");

        builder.HasIndex(e => e.EventDate)
            .HasDatabaseName("idx_events_date");

        builder.HasIndex(e => new { e.IsCompleted, e.EventDate })
            .HasDatabaseName("idx_events_upcoming");
    }
}
