using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ML;
using Polly;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Infrastructure.Data;
using PulpMXFantasy.Infrastructure.ExternalApi;
using PulpMXFantasy.Infrastructure.MachineLearning;
using PulpMXFantasy.Infrastructure.Services;
using PulpMXFantasy.ReadModel;

namespace PulpMXFantasy.Infrastructure;

/// <summary>
/// Dependency injection configuration for Infrastructure layer.
/// </summary>
/// <remarks>
/// SERVICES REGISTERED:
/// ====================
/// 1. **ApplicationDbContext** - Write model database (domain schema)
/// 2. **API Client** - PulpMX API with retry logic
/// 3. **Application Services** - Event sync, prediction service
/// 4. **ML Services** - LightGBM predictor with PredictionEnginePool
/// 5. **Team Optimizer** - Constraint programming for optimal team selection
/// 6. **ReadModelUpdater** - Updates CQRS read models
///
/// NOTE: ReadDbContext and ICommandStatusService are registered via AddReadModel().
/// Worker should call both AddInfrastructure() and AddReadModel().
/// </remarks>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all Infrastructure services with the DI container.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register write database context with PostgreSQL
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException(
                    "Database connection string 'DefaultConnection' is not configured. " +
                    "Set it in appsettings.json, User Secrets, or environment variable.");
            }

            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                // Configure migrations assembly
                npgsqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);

                // Store migrations history in the domain schema
                npgsqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "domain");

                // Enable query splitting for better performance with related data
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);

                // Enable retry on failure (transient PostgreSQL errors)
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorCodesToAdd: null);
            });

            // Development: Enable sensitive data logging and detailed errors
            if (configuration.GetValue<string>("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // Register PulpMX API client with retry policies
        services.AddPulpMXApiClient(configuration);

        // Register application services
        services.AddScoped<IEventSyncService, EventSyncService>();
        services.AddScoped<IPredictionService, PredictionService>();
        services.AddScoped<HistoricalDataImportService>();
        services.AddMemoryCache(); // Used by PredictionService for caching

        // Register ReadModelUpdater (requires ReadDbContext from ReadModel project)
        services.AddScoped<IReadModelUpdater, ReadModelUpdater>();
        services.AddScoped<ReadModelUpdater>(); // Also keep concrete registration for backward compatibility
        // NOTE: ICommandStatusService is registered via AddReadModel() in Worker/Web

        // Register repositories
        services.AddScoped<IEventRepository, Repositories.EventRepository>();

        // Register ML services
        services.AddMachineLearning(configuration);

        // Register team optimizer
        services.AddScoped<ITeamOptimizer, Optimization.TeamOptimizerService>();

        return services;
    }

    /// <summary>
    /// Registers PulpMX API client with Polly retry and circuit breaker policies.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    private static void AddPulpMXApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration options
        services.Configure<PulpMXApiOptions>(
            configuration.GetSection(PulpMXApiOptions.SectionName));

        // Validate that API token is configured
        var apiToken = configuration[$"{PulpMXApiOptions.SectionName}:ApiToken"];
        if (string.IsNullOrEmpty(apiToken))
        {
            throw new InvalidOperationException(
                "PulpMX API token is not configured. " +
                "Set it in User Secrets (development) or environment variable (production): " +
                "PulpMXApi:ApiToken=your-token-here");
        }

        // Register HttpClient with resilience policies
        services.AddHttpClient<IPulpMXApiClient, PulpMXApiClient>()
            .AddStandardResilienceHandler(options =>
            {
                // Configure retry policy
                options.Retry.MaxRetryAttempts = configuration.GetValue($"{PulpMXApiOptions.SectionName}:RetryCount", 3);
                options.Retry.Delay = TimeSpan.FromSeconds(2);
                options.Retry.BackoffType = DelayBackoffType.Exponential; // 2s, 4s, 8s
                options.Retry.UseJitter = true; // Add randomness to prevent thundering herd

                // Configure circuit breaker
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.FailureRatio = 0.5; // Break if 50% of requests fail
                options.CircuitBreaker.MinimumThroughput = 5; // Need at least 5 requests before breaking

                // Configure timeout
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(
                    configuration.GetValue($"{PulpMXApiOptions.SectionName}:TimeoutSeconds", 30));
            });
    }

    /// <summary>
    /// Registers machine learning services for fantasy point prediction.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <remarks>
    /// ML SERVICES REGISTERED:
    /// =======================
    /// 1. **PredictionEnginePool** - Thread-safe ML.NET prediction engine pool
    /// 2. **MultiStagePredictor** - Implements IRiderPredictor using qualification + finish models
    /// 3. **ModelTrainer** - Service for training and saving new models
    ///
    /// THREAD SAFETY:
    /// ==============
    /// PredictionEnginePool is CRITICAL for ASP.NET Core:
    /// - PredictionEngine is NOT thread-safe
    /// - Pool provides thread-safe, pooled access to prediction engines
    /// - Without pool, concurrent requests would crash or corrupt predictions
    ///
    /// MODEL LOADING:
    /// ==============
    /// Models loaded from disk using CONSISTENT FILE NAMES:
    /// - Path: ./TrainedModels/{BikeClass}_{ModelType}.zip
    /// - e.g., ./TrainedModels/Class250_Qualification.zip
    /// - watchForChanges: true enables automatic reloading when files are updated
    /// - If model doesn't exist, pool is registered but predictor uses fallback
    ///
    /// CRITICAL: File names must be consistent (not date-stamped) for watchForChanges to work!
    ///
    /// CONFIGURATION:
    /// ==============
    /// appsettings.json:
    /// {
    ///   "MLNet": {
    ///     "ModelDirectory": "./TrainedModels",
    ///     "RetrainIntervalHours": 24
    ///   }
    /// }
    /// </remarks>
    private static void AddMachineLearning(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var modelDirectory = configuration.GetValue("MLNet:ModelDirectory", "./TrainedModels");
        Directory.CreateDirectory(modelDirectory);

        // Model configurations using MLConstants for consistent naming
        var qualificationModels = new Dictionary<string, string>
        {
            { MachineLearning.MLConstants.Class250QualificationModel, Path.Combine(modelDirectory, MachineLearning.MLConstants.Class250QualificationFileName) },
            { MachineLearning.MLConstants.Class450QualificationModel, Path.Combine(modelDirectory, MachineLearning.MLConstants.Class450QualificationFileName) }
        };

        var finishPositionModels = new Dictionary<string, string>
        {
            { MachineLearning.MLConstants.Class250FinishPositionModel, Path.Combine(modelDirectory, MachineLearning.MLConstants.Class250FinishPositionFileName) },
            { MachineLearning.MLConstants.Class450FinishPositionModel, Path.Combine(modelDirectory, MachineLearning.MLConstants.Class450FinishPositionFileName) }
        };

        // Register PredictionEnginePool for qualification models
        // CRITICAL: watchForChanges:true enables automatic model reloading when files are updated
        var qualPoolBuilder = services.AddPredictionEnginePool<MachineLearning.QualificationModelData, MachineLearning.QualificationPrediction>();
        foreach (var (modelName, modelPath) in qualificationModels)
        {
            if (File.Exists(modelPath))
            {
                qualPoolBuilder.FromFile(modelName: modelName, filePath: modelPath, watchForChanges: true);
            }
        }

        // Register PredictionEnginePool for finish position models
        var finishPoolBuilder = services.AddPredictionEnginePool<MachineLearning.FinishPositionModelData, MachineLearning.FinishPositionPrediction>();
        foreach (var (modelName, modelPath) in finishPositionModels)
        {
            if (File.Exists(modelPath))
            {
                finishPoolBuilder.FromFile(modelName: modelName, filePath: modelPath, watchForChanges: true);
            }
        }

        // Track which models exist at startup for initial readiness check
        var existingQualModels = qualificationModels.Where(kv => File.Exists(kv.Value)).ToList();
        var existingFinishModels = finishPositionModels.Where(kv => File.Exists(kv.Value)).ToList();

        // Register multi-stage predictor implementation
        // Pass modelDirectory so predictor can check for newly-created model files
        services.AddSingleton<IRiderPredictor>(serviceProvider =>
        {
            var qualPool = serviceProvider.GetRequiredService<PredictionEnginePool<MachineLearning.QualificationModelData, MachineLearning.QualificationPrediction>>();
            var finishPool = serviceProvider.GetRequiredService<PredictionEnginePool<MachineLearning.FinishPositionModelData, MachineLearning.FinishPositionPrediction>>();
            var logger = serviceProvider.GetRequiredService<ILogger<MachineLearning.MultiStagePredictor>>();
            var predictor = new MachineLearning.MultiStagePredictor(qualPool, finishPool, logger, modelDirectory);

            // Log initial state
            if (existingQualModels.Any() && existingFinishModels.Any())
            {
                logger.LogInformation(
                    "MultiStagePredictor initialized with {QualCount} qualification and {FinishCount} finish position models",
                    existingQualModels.Count, existingFinishModels.Count);
            }
            else
            {
                logger.LogWarning(
                    "MultiStagePredictor starting without models. Train models to enable predictions. " +
                    "Found: {QualCount} qualification, {FinishCount} finish position",
                    existingQualModels.Count, existingFinishModels.Count);
            }

            return predictor;
        });

        // Register model trainer (used by background service and admin endpoints)
        services.AddScoped<IModelTrainer, ModelTrainer>();
        services.AddScoped<ModelTrainer>(); // Keep concrete registration for backward compatibility
    }
}
