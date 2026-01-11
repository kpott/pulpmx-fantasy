using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Data.Configurations;

/// <summary>
/// Entity Type Configuration for EventPredictionReadModel.
/// </summary>
public class EventPredictionConfiguration : IEntityTypeConfiguration<EventPredictionReadModel>
{
    public void Configure(EntityTypeBuilder<EventPredictionReadModel> builder)
    {
        builder.ToTable("event_predictions");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(e => e.EventId)
            .HasColumnName("event_id")
            .IsRequired();

        builder.Property(e => e.RiderId)
            .HasColumnName("rider_id")
            .IsRequired();

        builder.Property(e => e.RiderName)
            .HasColumnName("rider_name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.RiderNumber)
            .HasColumnName("rider_number")
            .IsRequired();

        builder.Property(e => e.BikeClass)
            .HasColumnName("bike_class")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.IsAllStar)
            .HasColumnName("is_all_star")
            .IsRequired();

        builder.Property(e => e.Handicap)
            .HasColumnName("handicap")
            .IsRequired();

        builder.Property(e => e.ExpectedPoints)
            .HasColumnName("expected_points")
            .IsRequired();

        builder.Property(e => e.PredictedFinish)
            .HasColumnName("predicted_finish");

        builder.Property(e => e.LowerBound)
            .HasColumnName("lower_bound")
            .IsRequired();

        builder.Property(e => e.UpperBound)
            .HasColumnName("upper_bound")
            .IsRequired();

        builder.Property(e => e.Confidence)
            .HasColumnName("confidence")
            .IsRequired();

        builder.Property(e => e.ModelVersion)
            .HasColumnName("model_version")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.GeneratedAt)
            .HasColumnName("generated_at")
            .IsRequired();

        // Indexes
        builder.HasIndex(e => new { e.EventId, e.RiderId })
            .IsUnique()
            .HasDatabaseName("uq_event_predictions_event_rider");

        builder.HasIndex(e => e.EventId)
            .HasDatabaseName("idx_event_predictions_event");

        builder.HasIndex(e => new { e.EventId, e.BikeClass })
            .HasDatabaseName("idx_event_predictions_class");

        builder.HasIndex(e => new { e.EventId, e.ExpectedPoints })
            .HasDatabaseName("idx_event_predictions_points");
    }
}
