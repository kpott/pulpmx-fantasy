using Microsoft.Extensions.DependencyInjection;

namespace PulpMXFantasy.Application;

/// <summary>
/// Dependency injection configuration for Application layer.
/// </summary>
/// <remarks>
/// WHY THIS IS MINIMAL:
/// ====================
/// In Clean Architecture, Application layer defines:
/// - **Interfaces** (contracts for services) - Registered in Infrastructure
/// - **DTOs** (data transfer objects) - No DI needed
/// - **Mappers** (static utility classes) - No DI needed
/// - **Use Cases** (future: commands/queries) - Would be registered here
///
/// CURRENT ARCHITECTURE:
/// =====================
/// - Application/Interfaces - IPredictionService (contract)
/// - Application/Mappers - ApiMapper (static utility)
/// - Infrastructure/Services - Service implementations
///
/// The service IMPLEMENTATIONS are registered in Infrastructure.AddInfrastructure():
/// - EventSyncService - Data sync orchestration
/// - PredictionService - ML prediction service
/// - MultiStagePredictor - ML model inference (qualification + finish position)
/// - ModelTrainer - ML model training
///
/// WHY IMPLEMENTATIONS IN INFRASTRUCTURE:
/// =======================================
/// They depend on infrastructure concerns:
/// - Database (ApplicationDbContext)
/// - External APIs (IPulpMXApiClient)
/// - ML libraries (ML.NET)
/// - Caching (IMemoryCache)
///
/// This keeps Application layer pure (no infrastructure dependencies).
///
/// FUTURE EXPANSION:
/// =================
/// When adding CQRS or MediatR:
/// - Add command/query handlers here
/// - Register them in this AddApplication() method
/// - Keep them free of infrastructure dependencies
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all Application services with the DI container.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for chaining</returns>
    /// <remarks>
    /// Currently empty because:
    /// - Interfaces are contracts (registered where implemented)
    /// - Mappers are static (no DI needed)
    /// - Service implementations are in Infrastructure layer
    ///
    /// Future: Register command/query handlers, validators, etc.
    /// </remarks>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Application layer currently only contains interfaces and mappers.
        // Service implementations are registered in Infrastructure layer.

        // Future: Register CQRS handlers, FluentValidation validators, etc.
        // Example:
        // services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        return services;
    }
}
