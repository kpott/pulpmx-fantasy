using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PulpMXFantasy.Infrastructure.Data;

/// <summary>
/// Factory for creating ApplicationDbContext instances at design time (for migrations).
/// </summary>
/// <remarks>
/// WHY THIS EXISTS:
/// ================
/// EF Core tools (dotnet ef migrations add/update) need to create a DbContext instance
/// to analyze the model and generate migration code. This factory provides that instance
/// when the application isn't running.
///
/// WHEN IS THIS USED:
/// ==================
/// - Running `dotnet ef migrations add InitialCreate`
/// - Running `dotnet ef database update`
/// - Running `dotnet ef migrations script`
///
/// This is ONLY for design-time tooling - runtime uses normal DI configuration.
///
/// CONNECTION STRING STRATEGY:
/// ===========================
/// 1. Try to read from environment variable (preferred for CI/CD)
/// 2. Fall back to default localhost connection for development
///
/// SECURITY NOTE:
/// ==============
/// This hardcoded fallback is OK because:
/// - Only used during development for local database
/// - Never deployed to production (Factory not called at runtime)
/// - Production uses connection string from Azure Key Vault via DI
///
/// USAGE:
/// ======
/// From Infrastructure project directory:
/// ```
/// dotnet ef migrations add InitialCreate
/// dotnet ef database update
/// ```
///
/// EF Core tools automatically discover and use this factory.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        // Try to get connection string from environment variable first
        // This allows CI/CD pipelines to provide their own connection strings
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DEFAULTCONNECTION");

        // Fall back to localhost development database if no environment variable set
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=pulpmx_fantasy;Username=postgres;Password=postgres";
            Console.WriteLine("INFO: Using default localhost connection string for EF Core design-time tools.");
            Console.WriteLine("      Set CONNECTIONSTRINGS__DEFAULTCONNECTION environment variable to override.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();

        // Configure PostgreSQL with Npgsql
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            // Configure migrations assembly (where migrations will be stored)
            npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);

            // Store migrations history in the domain schema
            npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "domain");

            // Map C# enums to PostgreSQL enum types
            // Npgsql handles this automatically, but we can customize if needed
            npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        });

        // Enable sensitive data logging for development (helps debug migrations)
        // This shows parameter values in logs - only OK for local development!
        optionsBuilder.EnableSensitiveDataLogging();
        optionsBuilder.EnableDetailedErrors();

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
