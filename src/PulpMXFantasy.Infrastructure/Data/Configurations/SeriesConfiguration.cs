using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Type Configuration for Series entity.
/// </summary>
/// <remarks>
/// SERIES TABLE STRUCTURE:
/// =======================
/// Configures the racing series table containing season information:
/// - Series identification (Name, SeriesType, Year)
/// - Date range (StartDate, EndDate)
/// - Status (IsActive)
/// - Relationships (many Events in the series)
///
/// CRITICAL BUSINESS RULES:
/// ========================
/// 1. Series enforces consecutive pick restrictions within same series
/// 2. Only one active series of each type at a time (enforced at application level)
/// 3. Series boundaries reset pick restrictions (SX → MX allows repeat picks)
///
/// INDEXING STRATEGY:
/// ==================
/// - Composite index on (Year, SeriesType) for "get 2025 Supercross" queries
/// - Index on IsActive for "get current active series" queries
/// </remarks>
public class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> builder)
    {
        // ==============================================================
        // TABLE AND PRIMARY KEY
        // ==============================================================

        builder.ToTable("series");

        builder.HasKey(s => s.Id);

        // ==============================================================
        // COLUMNS
        // ==============================================================

        builder.Property(s => s.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(s => s.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        // SeriesType enum → PostgreSQL enum type
        // Values: Supercross, Motocross, SuperMotocross
        builder.Property(s => s.SeriesType)
            .HasColumnName("series_type")
            .HasConversion<string>() // Store as string in PostgreSQL
            .IsRequired();

        builder.Property(s => s.Year)
            .HasColumnName("year")
            .IsRequired();

        builder.Property(s => s.StartDate)
            .HasColumnName("start_date")
            .IsRequired();

        builder.Property(s => s.EndDate)
            .HasColumnName("end_date");

        builder.Property(s => s.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // ==============================================================
        // INDEXES
        // ==============================================================

        // Composite index for "get 2025 Supercross" queries
        // Most common query pattern: WHERE year = @Year AND series_type = @SeriesType
        builder.HasIndex(s => new { s.Year, s.SeriesType })
            .HasDatabaseName("ix_series_year_type");

        // Index on IsActive for "get current active series" queries
        // Used to display current season in UI
        builder.HasIndex(s => s.IsActive)
            .HasDatabaseName("ix_series_is_active");

        // ==============================================================
        // RELATIONSHIPS
        // ==============================================================

        // One Series -> Many Events (series contains multiple race events)
        builder.HasMany(s => s.Events)
            .WithOne(e => e.Series)
            .HasForeignKey(e => e.SeriesId)
            .OnDelete(DeleteBehavior.Cascade); // If series deleted, delete all events in that series
    }
}
