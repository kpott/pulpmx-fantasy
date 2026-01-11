using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Type Configuration for Rider entity.
/// </summary>
/// <remarks>
/// WHY SEPARATE CONFIGURATION FILES?
/// ==================================
/// Following EF Core best practices:
/// 1. Keeps DbContext clean (no giant OnModelCreating method)
/// 2. Single responsibility - each configuration class maps one entity
/// 3. Easier to maintain and test configurations independently
/// 4. Follows Clean Architecture - infrastructure concerns stay in infrastructure
///
/// RIDER TABLE STRUCTURE:
/// ======================
/// This configures the master riders table containing:
/// - Rider identification (PulpMxId - stable API reference)
/// - Display information (Name, Number, PhotoUrl)
/// - Timestamps (CreatedAt, UpdatedAt)
/// - Relationships (many EventRider participations)
///
/// DESIGN DECISIONS:
/// =================
/// 1. Table name: "riders" (PostgreSQL convention: lowercase, plural)
/// 2. Primary key: UUID (PostgreSQL optimized, no collisions)
/// 3. Unique constraint on PulpMxId (API identifier must be unique)
/// 4. Name is required, max 100 characters
/// 5. PhotoUrl is optional, max 500 characters (URLs can be long)
/// </remarks>
public class RiderConfiguration : IEntityTypeConfiguration<Rider>
{
    public void Configure(EntityTypeBuilder<Rider> builder)
    {
        // ==============================================================
        // TABLE AND PRIMARY KEY
        // ==============================================================

        builder.ToTable("riders");

        builder.HasKey(r => r.Id);

        // ==============================================================
        // COLUMNS
        // ==============================================================

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(r => r.PulpMxId)
            .HasColumnName("pulp_mx_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.Number)
            .HasColumnName("number")
            .IsRequired();

        builder.Property(r => r.PhotoUrl)
            .HasColumnName("photo_url")
            .HasMaxLength(500);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // ==============================================================
        // INDEXES
        // ==============================================================

        // Unique index on PulpMxId for API synchronization lookups
        // Ensures each external rider ID appears only once in our database
        builder.HasIndex(r => r.PulpMxId)
            .IsUnique()
            .HasDatabaseName("ix_riders_pulp_mx_id");

        // Index on Name for search functionality
        // Enables fast "find rider by name" queries for UI autocomplete
        builder.HasIndex(r => r.Name)
            .HasDatabaseName("ix_riders_name");

        // ==============================================================
        // RELATIONSHIPS
        // ==============================================================

        // One Rider -> Many EventRiders (rider participates in many events)
        // Configured on EventRider side (inverse navigation)
        builder.HasMany(r => r.EventRiders)
            .WithOne(er => er.Rider)
            .HasForeignKey(er => er.RiderId)
            .OnDelete(DeleteBehavior.Cascade); // If rider deleted, delete all their event participations
    }
}
