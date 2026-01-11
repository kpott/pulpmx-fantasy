using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.ReadModel.Services;

namespace PulpMXFantasy.ReadModel;

/// <summary>
/// Dependency injection configuration for ReadModel layer.
/// </summary>
/// <remarks>
/// ASSEMBLY BOUNDARIES:
/// ====================
/// This extension registers ONLY read model services:
/// - ReadDbContext (read_model schema access)
/// - ICommandStatusService (status tracking)
///
/// It does NOT register:
/// - ApplicationDbContext (write models)
/// - ML services (IRiderPredictor, IModelTrainer)
/// - External API clients (IPulpMXApiClient)
/// - Write services (IEventSyncService, etc.)
///
/// This enforces CQRS boundaries at compile time.
/// Web project references ONLY this assembly for database access.
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers ReadModel services with the DI container.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddReadModel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string 'DefaultConnection' is not configured.");
        }

        // Register read model database context
        services.AddDbContext<ReadDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(ReadDbContext).Assembly.FullName);

                // Store migrations history in the read_model schema
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "read_model");
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery);
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            // Development: Enable sensitive data logging
            var environment = configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT")
                           ?? configuration.GetValue<string>("DOTNET_ENVIRONMENT");

            if (environment == "Development")
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // Register command status service
        services.AddScoped<ICommandStatusService, CommandStatusService>();

        return services;
    }
}
