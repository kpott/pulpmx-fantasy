using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel.Data.Configurations;

/// <summary>
/// Entity Type Configuration for CommandProgressHistoryReadModel.
/// </summary>
public class CommandProgressHistoryConfiguration : IEntityTypeConfiguration<CommandProgressHistoryReadModel>
{
    public void Configure(EntityTypeBuilder<CommandProgressHistoryReadModel> builder)
    {
        builder.ToTable("command_progress_history");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(c => c.CommandId)
            .HasColumnName("command_id")
            .IsRequired();

        builder.Property(c => c.Message)
            .HasColumnName("message")
            .IsRequired();

        builder.Property(c => c.ProgressPercentage)
            .HasColumnName("progress_percentage")
            .IsRequired();

        builder.Property(c => c.OccurredAt)
            .HasColumnName("occurred_at")
            .IsRequired();

        builder.Property(c => c.MilestoneName)
            .HasColumnName("milestone_name")
            .HasMaxLength(100);

        // Index for querying history by command
        builder.HasIndex(c => c.CommandId)
            .HasDatabaseName("idx_progress_history_command");
    }
}
