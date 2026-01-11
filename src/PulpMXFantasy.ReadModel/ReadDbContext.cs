using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Contracts.ReadModels;

namespace PulpMXFantasy.ReadModel;

/// <summary>
/// Entity Framework Core database context for CQRS read models.
/// </summary>
/// <remarks>
/// WHY SEPARATE READDBCONTEXT EXISTS:
/// ==================================
/// Following CQRS (Command Query Responsibility Segregation) pattern:
/// 1. Read models are optimized for queries, not writes
/// 2. Denormalized data avoids expensive joins
/// 3. Separate schema ("read_model") from write models ("public")
/// 4. Independent scaling and optimization opportunities
///
/// ASSEMBLY BOUNDARIES:
/// ====================
/// This context lives in PulpMXFantasy.ReadModel assembly which:
/// - Is the ONLY database access available to Web project
/// - Does NOT reference Infrastructure (no ML, no API, no writes)
/// - Enforces true CQRS separation at compile time
///
/// TABLES:
/// =======
/// - events: Denormalized event data for display
/// - event_predictions: ML predictions for UI display
/// - command_status: Track command execution progress
/// - model_metadata: ML model training metadata
/// </remarks>
public class ReadDbContext : DbContext
{
    /// <summary>
    /// Default schema name for read model tables.
    /// </summary>
    public const string SchemaName = "read_model";

    public ReadDbContext(DbContextOptions<ReadDbContext> options)
        : base(options)
    {
    }

    // ============================================================================
    // DBSETS - Each represents a read model table
    // ============================================================================

    /// <summary>
    /// Denormalized event data for UI display.
    /// Populated by Worker when events are synced.
    /// </summary>
    public DbSet<EventReadModel> Events => Set<EventReadModel>();

    /// <summary>
    /// ML predictions for event riders (denormalized for fast UI queries).
    /// </summary>
    public DbSet<EventPredictionReadModel> EventPredictions => Set<EventPredictionReadModel>();

    /// <summary>
    /// Command execution status tracking for async operations.
    /// </summary>
    public DbSet<CommandStatusReadModel> CommandStatus => Set<CommandStatusReadModel>();

    /// <summary>
    /// ML model training metadata and metrics.
    /// </summary>
    public DbSet<ModelMetadataReadModel> ModelMetadata => Set<ModelMetadataReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Set default schema for all read model tables
        modelBuilder.HasDefaultSchema(SchemaName);

        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReadDbContext).Assembly,
            type => type.Namespace?.Contains("Configurations") == true);
    }
}
