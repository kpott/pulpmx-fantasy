using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Data.Configurations;

/// <summary>
/// Entity Type Configuration for CommandStatusReadModel.
/// </summary>
public class CommandStatusConfiguration : IEntityTypeConfiguration<CommandStatusReadModel>
{
    public void Configure(EntityTypeBuilder<CommandStatusReadModel> builder)
    {
        builder.ToTable("command_status");

        builder.HasKey(c => c.CommandId);

        builder.Property(c => c.CommandId)
            .HasColumnName("command_id")
            .IsRequired();

        builder.Property(c => c.CorrelationId)
            .HasColumnName("correlation_id")
            .IsRequired();

        builder.Property(c => c.CommandType)
            .HasColumnName("command_type")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.ProgressMessage)
            .HasColumnName("progress_message");

        builder.Property(c => c.ProgressPercentage)
            .HasColumnName("progress_percentage");

        builder.Property(c => c.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(c => c.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(c => c.ErrorMessage)
            .HasColumnName("error_message");

        builder.Property(c => c.ResultData)
            .HasColumnName("result_data")
            .HasColumnType("jsonb");

        builder.Property(c => c.DurationMs)
            .HasColumnName("duration_ms");

        // Indexes
        builder.HasIndex(c => c.CorrelationId)
            .HasDatabaseName("idx_command_status_correlation");

        builder.HasIndex(c => new { c.CommandType, c.Status })
            .HasDatabaseName("idx_command_status_type");

        builder.HasIndex(c => c.StartedAt)
            .HasDatabaseName("idx_command_status_started");
    }
}
