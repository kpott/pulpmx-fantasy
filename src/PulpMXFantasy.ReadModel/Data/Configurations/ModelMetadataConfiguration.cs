using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Data.Configurations;

/// <summary>
/// Entity Type Configuration for ModelMetadataReadModel.
/// </summary>
public class ModelMetadataConfiguration : IEntityTypeConfiguration<ModelMetadataReadModel>
{
    public void Configure(EntityTypeBuilder<ModelMetadataReadModel> builder)
    {
        builder.ToTable("model_metadata");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(m => m.BikeClass)
            .HasColumnName("bike_class")
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(m => m.ModelType)
            .HasColumnName("model_type")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.Version)
            .HasColumnName("version")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(m => m.TrainedAt)
            .HasColumnName("trained_at")
            .IsRequired();

        builder.Property(m => m.TrainingSamples)
            .HasColumnName("training_samples")
            .IsRequired();

        builder.Property(m => m.ValidationAccuracy)
            .HasColumnName("validation_accuracy");

        builder.Property(m => m.RSquared)
            .HasColumnName("r_squared");

        builder.Property(m => m.MeanAbsoluteError)
            .HasColumnName("mean_absolute_error");

        builder.Property(m => m.ModelPath)
            .HasColumnName("model_path")
            .IsRequired();

        builder.Property(m => m.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        // Indexes
        builder.HasIndex(m => new { m.BikeClass, m.ModelType, m.Version })
            .IsUnique()
            .HasDatabaseName("uq_model_metadata_version");

        builder.HasIndex(m => new { m.BikeClass, m.ModelType, m.IsActive })
            .HasDatabaseName("idx_model_metadata_active");

        builder.HasIndex(m => m.TrainedAt)
            .HasDatabaseName("idx_model_metadata_trained");
    }
}
