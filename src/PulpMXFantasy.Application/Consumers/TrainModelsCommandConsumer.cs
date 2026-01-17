using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;
using System.Text.Json;

namespace PulpMXFantasy.Application.Consumers;

/// <summary>
/// Handles TrainModelsCommand by training all 4 ML models sequentially.
/// </summary>
/// <remarks>
/// WHY THIS HANDLER EXISTS:
/// ========================
/// Encapsulates the entire ML training workflow as a single command:
/// 1. Syncs next event from API (ensures latest data)
/// 2. Publishes status events for real-time UI updates via SignalR
/// 3. Trains 4 models sequentially (250/450 x Qualification/FinishPosition)
/// 4. Persists ModelMetadata to read model for each trained model
/// 5. Publishes ModelsTrainedEvent on completion (triggers prediction regeneration)
///
/// TRAINING SEQUENCE:
/// ==================
/// 0. Sync next event from PulpMX API
/// 1. 250cc Qualification (binary classification)
/// 2. 250cc FinishPosition (regression)
/// 3. 450cc Qualification (binary classification)
/// 4. 450cc FinishPosition (regression)
///
/// PROGRESS TRACKING (EVENT-DRIVEN):
/// =================================
/// This is the longest-running command (60-300 seconds).
/// Publishes CommandProgressUpdatedEvent after each step:
/// - 0%: Syncing next event
/// - 10%: Training 250cc Qualification
/// - 30%: Training 250cc FinishPosition
/// - 55%: Training 450cc Qualification
/// - 80%: Training 450cc FinishPosition
/// - 100%: Completed
///
/// ERROR HANDLING:
/// ===============
/// If any model training fails, publishes CommandFailedEvent.
/// Partial training results are NOT published - all 4 models must succeed.
///
/// USAGE:
/// ======
/// Triggered by:
/// - Admin UI "Train Models" button
/// - Scheduled background job (daily/weekly)
/// - After importing historical data
/// </remarks>
public class TrainModelsCommandConsumer : IConsumer<TrainModelsCommand>
{
    private readonly IEventSyncService _eventSyncService;
    private readonly IModelTrainer _modelTrainer;
    private readonly IReadModelUpdater _readModelUpdater;
    private readonly IRiderPredictor _riderPredictor;
    private readonly ILogger<TrainModelsCommandConsumer> _logger;
    private readonly string _modelDirectory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a new TrainModelsCommandConsumer instance.
    /// </summary>
    public TrainModelsCommandConsumer(
        IEventSyncService eventSyncService,
        IModelTrainer modelTrainer,
        IReadModelUpdater readModelUpdater,
        IRiderPredictor riderPredictor,
        ILogger<TrainModelsCommandConsumer> logger,
        IConfiguration? configuration = null)
    {
        _eventSyncService = eventSyncService ?? throw new ArgumentNullException(nameof(eventSyncService));
        _modelTrainer = modelTrainer ?? throw new ArgumentNullException(nameof(modelTrainer));
        _readModelUpdater = readModelUpdater ?? throw new ArgumentNullException(nameof(readModelUpdater));
        _riderPredictor = riderPredictor ?? throw new ArgumentNullException(nameof(riderPredictor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelDirectory = configuration?["MLNet:ModelDirectory"] ?? "./TrainedModels";
    }

    /// <summary>
    /// Handles the TrainModelsCommand by training all 4 ML models.
    /// </summary>
    /// <param name="context">Message consume context</param>
    public async Task Consume(ConsumeContext<TrainModelsCommand> context)
    {
        var command = context.Message;
        var commandId = context.MessageId ?? Guid.NewGuid();
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation(
            "Starting TrainModelsCommand: CommandId={CommandId}, CorrelationId={CorrelationId}, Force={Force}",
            commandId, context.CorrelationId, command.Force);

        // Publish CommandStartedEvent
        await context.Publish(new CommandStartedEvent(
            commandId,
            "TrainModels",
            DateTimeOffset.UtcNow), cancellationToken);

        try
        {
            // Step 0: Sync next event first to ensure we have latest data
            await context.Publish(new CommandProgressUpdatedEvent(
                commandId,
                "Syncing next event from API...",
                0,
                DateTimeOffset.UtcNow,
                "SyncEvent"), cancellationToken);

            var synced = await _eventSyncService.SyncNextEventAsync(cancellationToken);
            _logger.LogInformation("Event sync completed: Synced={Synced}", synced);

            var trainedModels = new List<TrainedModelResult>();

            // Train all 4 models sequentially
            // 1. 250cc Qualification (10%)
            await context.Publish(new CommandProgressUpdatedEvent(
                commandId,
                "Training 250cc Qualification model...",
                10,
                DateTimeOffset.UtcNow,
                "Train250Qual"), cancellationToken);

            var class250Qual = await _modelTrainer.TrainQualificationModelAsync(
                BikeClass.Class250,
                _modelDirectory,
                cancellationToken);
            trainedModels.Add(class250Qual);
            await PersistModelMetadataAsync(class250Qual, cancellationToken);

            // 2. 250cc FinishPosition (30%)
            await context.Publish(new CommandProgressUpdatedEvent(
                commandId,
                "Training 250cc FinishPosition model...",
                30,
                DateTimeOffset.UtcNow,
                "Train250Finish"), cancellationToken);

            var class250Finish = await _modelTrainer.TrainFinishPositionModelAsync(
                BikeClass.Class250,
                _modelDirectory,
                cancellationToken);
            trainedModels.Add(class250Finish);
            await PersistModelMetadataAsync(class250Finish, cancellationToken);

            // 3. 450cc Qualification (55%)
            await context.Publish(new CommandProgressUpdatedEvent(
                commandId,
                "Training 450cc Qualification model...",
                55,
                DateTimeOffset.UtcNow,
                "Train450Qual"), cancellationToken);

            var class450Qual = await _modelTrainer.TrainQualificationModelAsync(
                BikeClass.Class450,
                _modelDirectory,
                cancellationToken);
            trainedModels.Add(class450Qual);
            await PersistModelMetadataAsync(class450Qual, cancellationToken);

            // 4. 450cc FinishPosition (80%)
            await context.Publish(new CommandProgressUpdatedEvent(
                commandId,
                "Training 450cc FinishPosition model...",
                80,
                DateTimeOffset.UtcNow,
                "Train450Finish"), cancellationToken);

            var class450Finish = await _modelTrainer.TrainFinishPositionModelAsync(
                BikeClass.Class450,
                _modelDirectory,
                cancellationToken);
            trainedModels.Add(class450Finish);
            await PersistModelMetadataAsync(class450Finish, cancellationToken);

            // Calculate total training samples
            var totalSamples = trainedModels.Sum(m => m.TrainingSamples);

            // CRITICAL: Force reload models in the predictor so it uses the new models
            // This bypasses PredictionEnginePool's unreliable file watching
            _logger.LogInformation("Reloading models in predictor after training...");
            _riderPredictor.ReloadModels();
            _logger.LogInformation("Models reloaded successfully");

            // Publish ModelsTrainedEvent (domain event for downstream workflows)
            var modelsTrainedEvent = new ModelsTrainedEvent(
                TrainedAt: DateTimeOffset.UtcNow,
                Models: trainedModels.Select(m => new ModelMetadata(
                    BikeClass: m.BikeClass,
                    ModelType: m.ModelType,
                    Version: m.Version,
                    ValidationAccuracy: (float)m.ValidationAccuracy,
                    RSquared: (float)m.RSquared,
                    MeanAbsoluteError: (float)m.MeanAbsoluteError,
                    ModelPath: m.ModelPath
                )).ToList(),
                TotalTrainingSamples: totalSamples);

            await context.Publish(modelsTrainedEvent, cancellationToken);

            _logger.LogInformation(
                "Published ModelsTrainedEvent: Models={ModelCount}, TotalSamples={TotalSamples}",
                trainedModels.Count, totalSamples);

            // Publish CommandCompletedEvent
            var resultData = new
            {
                ModelsCount = trainedModels.Count,
                TotalSamples = totalSamples,
                Models = trainedModels.Select(m => new
                {
                    m.BikeClass,
                    m.ModelType,
                    m.Version,
                    m.RSquared,
                    m.MeanAbsoluteError,
                    m.ValidationAccuracy
                })
            };

            var completionMessage = $"Trained {trainedModels.Count} models ({totalSamples:N0} samples)";
            await context.Publish(new CommandCompletedEvent(
                commandId,
                DateTimeOffset.UtcNow,
                completionMessage,
                JsonSerializer.Serialize(resultData, JsonOptions)), cancellationToken);

            _logger.LogInformation(
                "TrainModelsCommand completed successfully: CommandId={CommandId}, ModelsCount={ModelsCount}",
                commandId, trainedModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TrainModelsCommand failed: CommandId={CommandId}, Error={Error}",
                commandId, ex.Message);

            // Publish CommandFailedEvent - do NOT rethrow to prevent MassTransit retry
            await context.Publish(new CommandFailedEvent(
                commandId,
                DateTimeOffset.UtcNow,
                $"Model training failed: {ex.Message}",
                ex.GetType().Name), cancellationToken);
        }
    }

    /// <summary>
    /// Persists model metadata to the read model database.
    /// </summary>
    private async Task PersistModelMetadataAsync(
        TrainedModelResult result,
        CancellationToken cancellationToken)
    {
        var metadata = new ModelMetadataReadModel
        {
            Id = Guid.NewGuid(),
            BikeClass = result.BikeClass,
            ModelType = result.ModelType,
            Version = result.Version,
            TrainedAt = result.TrainedAt,
            TrainingSamples = result.TrainingSamples,
            ValidationAccuracy = (float)result.ValidationAccuracy,
            RSquared = (float)result.RSquared,
            MeanAbsoluteError = (float)result.MeanAbsoluteError,
            ModelPath = result.ModelPath,
            IsActive = true
        };

        await _readModelUpdater.UpdateModelMetadataAsync(metadata, cancellationToken);

        _logger.LogDebug(
            "Persisted model metadata: BikeClass={BikeClass}, ModelType={ModelType}, Version={Version}",
            result.BikeClass, result.ModelType, result.Version);
    }
}
