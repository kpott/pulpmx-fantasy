using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Type Configuration for TeamSelection entity.
/// </summary>
/// <remarks>
/// TEAMSELECTION TABLE STRUCTURE:
/// ==============================
/// Configures the team rider selections table (join table):
/// - Team linkage (TeamId)
/// - Rider linkage (EventRiderId)
/// - Optional ordering (SelectionOrder for UI display)
///
/// RELATIONSHIP STRUCTURE:
/// =======================
/// Team (1) ←→ (8) TeamSelection (M) ←→ (1) EventRider
///
/// - ONE team has EXACTLY 8 team selections
/// - ONE team selection links ONE event rider to ONE team
/// - ONE event rider can be on MANY teams (multiple users pick same rider)
///
/// BUSINESS RULES:
/// ===============
/// - Each team must have exactly 8 team selections
/// - 4 selections from 250 class, 4 from 450 class
/// - Exactly 1 All-Star per class
/// - No duplicate riders on same team
///
/// TYPICAL DATA VOLUME:
/// ====================
/// - 8 selections per team
/// - If 1000 users create teams per event = 8,000 TeamSelection records per event
/// - 32 events per year = ~256,000 TeamSelection records per year
///
/// INDEXING STRATEGY:
/// ==================
/// - Index on TeamId for "get all riders on team" queries
/// - Index on EventRiderId for analytics ("how many users picked this rider?")
/// - Unique composite index on (TeamId, EventRiderId) to prevent duplicates
/// </remarks>
public class TeamSelectionConfiguration : IEntityTypeConfiguration<TeamSelection>
{
    public void Configure(EntityTypeBuilder<TeamSelection> builder)
    {
        // ==============================================================
        // TABLE AND PRIMARY KEY
        // ==============================================================

        builder.ToTable("team_selections");

        builder.HasKey(ts => ts.Id);

        // ==============================================================
        // COLUMNS
        // ==============================================================

        builder.Property(ts => ts.Id)
            .HasColumnName("id")
            .IsRequired();

        builder.Property(ts => ts.TeamId)
            .HasColumnName("team_id")
            .IsRequired();

        builder.Property(ts => ts.EventRiderId)
            .HasColumnName("event_rider_id")
            .IsRequired();

        // SelectionOrder: Optional ordering for UI display (1-8)
        // Could be used for position-based strategies in future
        builder.Property(ts => ts.SelectionOrder)
            .HasColumnName("selection_order");

        builder.Property(ts => ts.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // ==============================================================
        // INDEXES
        // ==============================================================

        // Index on TeamId for "get all riders on team" queries
        // Query pattern: WHERE team_id = @TeamId
        // Most common use case: Display team roster
        builder.HasIndex(ts => ts.TeamId)
            .HasDatabaseName("ix_team_selections_team_id");

        // Index on EventRiderId for analytics
        // Query pattern: WHERE event_rider_id = @EventRiderId
        // Use case: "How many users picked Chase Sexton?"
        builder.HasIndex(ts => ts.EventRiderId)
            .HasDatabaseName("ix_team_selections_event_rider_id");

        // Unique composite index on (TeamId, EventRiderId)
        // Business rule: Can't pick same rider twice on same team
        // Prevents duplicate selections
        builder.HasIndex(ts => new { ts.TeamId, ts.EventRiderId })
            .IsUnique()
            .HasDatabaseName("ix_team_selections_team_rider_unique");

        // ==============================================================
        // RELATIONSHIPS
        // ==============================================================

        // Many TeamSelections -> One Team (selection belongs to one team)
        builder.HasOne(ts => ts.Team)
            .WithMany(t => t.TeamSelections)
            .HasForeignKey(ts => ts.TeamId)
            .OnDelete(DeleteBehavior.Cascade); // If team deleted, delete all selections

        // Many TeamSelections -> One EventRider (selection links to one event rider)
        builder.HasOne(ts => ts.EventRider)
            .WithMany() // No inverse navigation on EventRider
            .HasForeignKey(ts => ts.EventRiderId)
            .OnDelete(DeleteBehavior.Cascade); // If event rider deleted, cascade delete
    }
}
