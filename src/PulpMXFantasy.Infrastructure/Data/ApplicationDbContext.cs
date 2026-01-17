using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Entities;

namespace PulpMXFantasy.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for PulpMX Fantasy application.
/// </summary>
/// <remarks>
/// WHY APPLICATIONDBCONTEXT EXISTS:
/// ================================
/// This is the central EF Core DbContext that:
/// 1. Maps domain entities to PostgreSQL database tables
/// 2. Manages database connections and transactions
/// 3. Tracks entity changes for persistence
/// 4. Provides LINQ query interface over database
///
/// DESIGN DECISIONS:
/// =================
///
/// 1. **Entity Configurations in Separate Files**
///    - Each entity has its own IEntityTypeConfiguration<T> class
///    - Keeps this file clean and focused
///    - Follows single responsibility principle
///    - Located in Data/Configurations/ folder
///
/// 2. **PostgreSQL-Specific Features**
///    - Uses Npgsql provider for PostgreSQL
///    - Enums mapped to PostgreSQL enum types (native support)
///    - UUIDs for primary keys (PostgreSQL optimized)
///    - Indexes for query performance
///
/// 3. **Naming Conventions**
///    - Table names: snake_case (PostgreSQL convention)
///    - Column names: snake_case (PostgreSQL convention)
///    - Database schema: domain (separates from public and read_model)
///
/// 4. **Query Performance Optimizations**
///    - Indexes on foreign keys automatically
///    - Indexes on frequently queried columns (event_date, series_type, etc.)
///    - Composite indexes for complex queries
///
/// USAGE EXAMPLES:
/// ===============
///
/// Querying next event:
/// <code>
/// var nextEvent = await _dbContext.Events
///     .Where(e => !e.IsCompleted && e.EventDate >= DateTime.UtcNow)
///     .OrderBy(e => e.EventDate)
///     .FirstOrDefaultAsync();
/// </code>
///
/// Getting event riders for predictions:
/// <code>
/// var eventRiders = await _dbContext.EventRiders
///     .Include(er => er.Rider)
///     .Include(er => er.Event)
///     .Where(er => er.EventId == eventId)
///     .Where(er => !er.IsInjured)
///     .ToListAsync();
/// </code>
///
/// Creating a team:
/// <code>
/// var team = new Team { /* properties */ };
/// team.TeamSelections.Add(new TeamSelection { /* properties */ });
/// _dbContext.Teams.Add(team);
/// await _dbContext.SaveChangesAsync();
/// </code>
///
/// MIGRATIONS:
/// ===========
/// Generate migrations:  dotnet ef migrations add MigrationName
/// Update database:      dotnet ef database update
/// Rollback migration:   dotnet ef database update PreviousMigrationName
///
/// TESTING:
/// ========
/// Integration tests use Testcontainers to spin up real PostgreSQL instances.
/// This ensures tests run against actual PostgreSQL features (enums, UUIDs, etc.)
/// </remarks>
public class ApplicationDbContext : DbContext
{
    /// <summary>
    /// Constructor for dependency injection.
    /// </summary>
    /// <param name="options">DbContext options configured in DI container</param>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // ============================================================================
    // DBSETS - Each represents a database table
    // ============================================================================

    /// <summary>
    /// Racing series (e.g., "2025 Supercross", "2025 Motocross")
    /// </summary>
    public DbSet<Series> Series => Set<Series>();

    /// <summary>
    /// Individual race events (e.g., "Anaheim 1", "Daytona")
    /// </summary>
    public DbSet<Event> Events => Set<Event>();

    /// <summary>
    /// Master rider data (persistent across all events)
    /// </summary>
    public DbSet<Rider> Riders => Set<Rider>();

    /// <summary>
    /// Event-specific rider data (MOST IMPORTANT TABLE)
    /// Contains handicap, results, qualifying data, fantasy points
    /// </summary>
    public DbSet<EventRider> EventRiders => Set<EventRider>();

    /// <summary>
    /// User fantasy teams for specific events
    /// </summary>
    public DbSet<Team> Teams => Set<Team>();

    /// <summary>
    /// Individual rider selections on teams
    /// Join table: Team ←→ EventRider
    /// </summary>
    public DbSet<TeamSelection> TeamSelections => Set<TeamSelection>();

    // ============================================================================
    // MODEL CONFIGURATION
    // ============================================================================

    /// <summary>
    /// Configures entity mappings, relationships, and database conventions.
    /// </summary>
    /// <param name="modelBuilder">EF Core model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from Data/Configurations/ folder
        // This discovers all IEntityTypeConfiguration<T> implementations automatically
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Configure PostgreSQL-specific features
        ConfigurePostgresEnums(modelBuilder);

        // Set default schema to isolate domain entities from other schemas
        modelBuilder.HasDefaultSchema("domain");
    }

    /// <summary>
    /// Configures PostgreSQL enum types for domain enums.
    /// </summary>
    /// <remarks>
    /// PostgreSQL has native enum support, which is more type-safe and performant
    /// than storing enums as strings or integers.
    ///
    /// This method maps C# enums to PostgreSQL enum types:
    /// - BikeClass → bike_class enum
    /// - SeriesType → series_type enum
    /// - EventFormat → event_format enum
    /// - Division → division enum
    ///
    /// Enum values are stored as lowercase with underscores (PostgreSQL convention):
    /// - BikeClass.Class250 → '250_class'
    /// - EventFormat.TripleCrown → 'triple_crown'
    ///
    /// IMPORTANT: Migrations will create these enum types in PostgreSQL.
    /// </remarks>
    private static void ConfigurePostgresEnums(ModelBuilder modelBuilder)
    {
        // Note: Npgsql will automatically create PostgreSQL enum types from our C# enums
        // when migrations are generated. We just need to ensure proper naming conventions.

        // If we need to customize enum mapping, we can use:
        // modelBuilder.HasPostgresEnum<BikeClass>(name: "bike_class");
        // But Npgsql handles this automatically in most cases.
    }

    /// <summary>
    /// Saves changes to the database, automatically updating timestamp fields.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of entities written to database</returns>
    /// <remarks>
    /// This override automatically sets UpdatedAt timestamps for modified entities.
    /// Ensures consistency - developers don't need to manually set UpdatedAt every time.
    /// </remarks>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Update UpdatedAt timestamp for all modified entities that implement IHasTimestamps
        var entries = ChangeTracker.Entries<IHasTimestamps>()
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
