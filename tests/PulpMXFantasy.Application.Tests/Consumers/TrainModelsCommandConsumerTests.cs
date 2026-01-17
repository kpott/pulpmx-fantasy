using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;
using PulpMXFantasy.Contracts.ReadModels;
using PulpMXFantasy.Domain.Abstractions;
using PulpMXFantasy.Domain.Enums;

namespace PulpMXFantasy.Application.Tests.Consumers;

/// <summary>
/// Unit tests for TrainModelsCommandConsumer following TDD methodology.
/// </summary>
/// <remarks>
/// WHAT THIS TESTS:
/// ================
/// 1. Handler trains all 4 models (250/450 x Qualification/FinishPosition)
/// 2. Handler publishes CommandProgressUpdatedEvent between each model training
/// 3. Handler persists ModelMetadata to read model
/// 4. Handler publishes ModelsTrainedEvent with all metrics
/// 5. Error during training publishes CommandFailedEvent
/// 6. Force flag retrains even if recent models exist
///
/// EVENT-DRIVEN ARCHITECTURE:
/// ==========================
/// The consumer publishes events via context.Publish() instead of calling
/// ICommandStatusService directly. This allows the Web project's consumer
/// to handle status updates and push to SignalR.
/// </remarks>
public class TrainModelsCommandConsumerTests
{
    private readonly IEventSyncService _mockEventSyncService;
    private readonly IModelTrainer _mockModelTrainer;
    private readonly IReadModelUpdater _mockReadModelUpdater;
    private readonly IRiderPredictor _mockRiderPredictor;
    private readonly ILogger<TrainModelsCommandConsumer> _mockLogger;
    private readonly ConsumeContext<TrainModelsCommand> _mockConsumeContext;

    public TrainModelsCommandConsumerTests()
    {
        _mockEventSyncService = Substitute.For<IEventSyncService>();
        _mockModelTrainer = Substitute.For<IModelTrainer>();
        _mockReadModelUpdater = Substitute.For<IReadModelUpdater>();
        _mockRiderPredictor = Substitute.For<IRiderPredictor>();
        _mockLogger = Substitute.For<ILogger<TrainModelsCommandConsumer>>();
        _mockConsumeContext = Substitute.For<ConsumeContext<TrainModelsCommand>>();

        // Default: sync succeeds
        _mockEventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>()).Returns(true);
    }

    private TrainModelsCommandConsumer CreateHandler() =>
        new TrainModelsCommandConsumer(
            _mockEventSyncService,
            _mockModelTrainer,
            _mockReadModelUpdater,
            _mockRiderPredictor,
            _mockLogger);

    /// <summary>
    /// Test: Handler trains all 4 models (250/450, Qual/Finish)
    /// </summary>
    [Fact]
    public async Task Consume_TrainsAllFourModels_WhenCommandReceived()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - Verify all 4 models were trained
        await _mockModelTrainer.Received(1).TrainQualificationModelAsync(
            BikeClass.Class250,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await _mockModelTrainer.Received(1).TrainFinishPositionModelAsync(
            BikeClass.Class250,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await _mockModelTrainer.Received(1).TrainQualificationModelAsync(
            BikeClass.Class450,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await _mockModelTrainer.Received(1).TrainFinishPositionModelAsync(
            BikeClass.Class450,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Handler publishes CommandStartedEvent at the start
    /// </summary>
    [Fact]
    public async Task Consume_PublishesCommandStartedEvent_AtStart()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.CorrelationId.Returns(correlationId);
        _mockConsumeContext.MessageId.Returns(commandId);

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert
        await _mockConsumeContext.Received(1).Publish(
            Arg.Is<CommandStartedEvent>(e =>
                e.CommandId == commandId &&
                e.CommandType == "TrainModels"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Handler publishes progress events between each model training step.
    /// Progress: 0% (sync), 10% (250 qual), 30% (250 finish), 55% (450 qual), 80% (450 finish)
    /// </summary>
    [Fact]
    public async Task Consume_PublishesProgressEventsBetweenEachModelTraining()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.CorrelationId.Returns(correlationId);
        _mockConsumeContext.MessageId.Returns(commandId);

        SetupSuccessfulModelTraining();

        var progressEvents = new List<CommandProgressUpdatedEvent>();
        _mockConsumeContext.Publish(
            Arg.Do<CommandProgressUpdatedEvent>(e => progressEvents.Add(e)),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - Verify progress updates were made at 0% (sync), 10%, 30%, 55%, 80%
        await _mockConsumeContext.Received().Publish(
            Arg.Any<CommandProgressUpdatedEvent>(),
            Arg.Any<CancellationToken>());

        Assert.Contains(progressEvents, p => p.ProgressPercentage == 0 && p.ProgressMessage.Contains("Syncing"));
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 10 && p.ProgressMessage.Contains("250") && p.ProgressMessage.Contains("Qualification"));
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 30 && p.ProgressMessage.Contains("250") && p.ProgressMessage.Contains("Finish"));
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 55 && p.ProgressMessage.Contains("450") && p.ProgressMessage.Contains("Qualification"));
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 80 && p.ProgressMessage.Contains("450") && p.ProgressMessage.Contains("Finish"));
    }

    /// <summary>
    /// Test: Handler persists ModelMetadata to read model for each trained model
    /// </summary>
    [Fact]
    public async Task Consume_PersistsModelMetadataToReadModel_ForEachTrainedModel()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - Verify 4 model metadata records were saved
        await _mockReadModelUpdater.Received(4).UpdateModelMetadataAsync(
            Arg.Any<ModelMetadataReadModel>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Handler publishes ModelsTrainedEvent with all metrics on completion
    /// </summary>
    [Fact]
    public async Task Consume_PublishesModelsTrainedEvent_OnSuccessfulCompletion()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - ModelsTrainedEvent published via context
        await _mockConsumeContext.Received(1).Publish(
            Arg.Is<ModelsTrainedEvent>(e =>
                e.Models.Count == 4 &&
                e.TotalTrainingSamples > 0),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Error during training publishes CommandFailedEvent
    /// </summary>
    [Fact]
    public async Task Consume_PublishesCommandFailedEvent_WhenTrainingThrowsException()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.CorrelationId.Returns(correlationId);
        _mockConsumeContext.MessageId.Returns(commandId);

        var errorMessage = "Insufficient training data for Class250 qualification model";
        _mockModelTrainer.TrainQualificationModelAsync(
            Arg.Any<BikeClass>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(errorMessage));

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - CommandFailedEvent published
        await _mockConsumeContext.Received(1).Publish(
            Arg.Is<CommandFailedEvent>(e =>
                e.CommandId == commandId &&
                e.ErrorMessage.Contains(errorMessage)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Force flag retrains even if recent models exist (always trains all 4 models)
    /// </summary>
    [Fact]
    public async Task Consume_RetrainsAllModels_WhenForceIsTrue()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow, Force: true);
        var correlationId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - All 4 models should be trained (2 qualification + 2 finish position)
        await _mockModelTrainer.Received(2).TrainQualificationModelAsync(
            Arg.Any<BikeClass>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _mockModelTrainer.Received(2).TrainFinishPositionModelAsync(
            Arg.Any<BikeClass>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Handler publishes CommandCompletedEvent on success
    /// </summary>
    [Fact]
    public async Task Consume_PublishesCommandCompletedEvent_OnSuccess()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();
        var commandId = Guid.NewGuid();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(commandId);
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert
        await _mockConsumeContext.Received(1).Publish(
            Arg.Is<CommandCompletedEvent>(e => e.CommandId == commandId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Test: Models are trained sequentially in correct order
    /// </summary>
    [Fact]
    public async Task Consume_TrainsModelsSequentially_InCorrectOrder()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();
        var trainingOrder = new List<string>();

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        _mockModelTrainer.TrainQualificationModelAsync(
            Arg.Any<BikeClass>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                var bikeClass = callInfo.ArgAt<BikeClass>(0);
                trainingOrder.Add($"{bikeClass}_Qualification");
                return Task.FromResult(CreateTrainedModelResult(bikeClass, "Qualification"));
            });

        _mockModelTrainer.TrainFinishPositionModelAsync(
            Arg.Any<BikeClass>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .ReturnsForAnyArgs(callInfo =>
            {
                var bikeClass = callInfo.ArgAt<BikeClass>(0);
                trainingOrder.Add($"{bikeClass}_FinishPosition");
                return Task.FromResult(CreateTrainedModelResult(bikeClass, "FinishPosition"));
            });

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - Order should be: 250 Qual, 250 Finish, 450 Qual, 450 Finish
        Assert.Equal(4, trainingOrder.Count);
        Assert.Equal("Class250_Qualification", trainingOrder[0]);
        Assert.Equal("Class250_FinishPosition", trainingOrder[1]);
        Assert.Equal("Class450_Qualification", trainingOrder[2]);
        Assert.Equal("Class450_FinishPosition", trainingOrder[3]);
    }

    /// <summary>
    /// Test: ModelsTrainedEvent contains correct model count and total samples
    /// </summary>
    [Fact]
    public async Task Consume_PublishesEventWithCorrectMetrics()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();
        ModelsTrainedEvent? publishedEvent = null;

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(correlationId);

        SetupSuccessfulModelTraining();

        _mockConsumeContext.Publish(
            Arg.Do<ModelsTrainedEvent>(e => publishedEvent = e),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert
        Assert.NotNull(publishedEvent);
        Assert.Equal(4, publishedEvent.Models.Count);
        Assert.True(publishedEvent.TotalTrainingSamples > 0);
        Assert.True(publishedEvent.TrainedAt <= DateTimeOffset.UtcNow);

        // Verify all 4 model types are present
        Assert.Contains(publishedEvent.Models, m => m.BikeClass == "Class250" && m.ModelType == "Qualification");
        Assert.Contains(publishedEvent.Models, m => m.BikeClass == "Class250" && m.ModelType == "FinishPosition");
        Assert.Contains(publishedEvent.Models, m => m.BikeClass == "Class450" && m.ModelType == "Qualification");
        Assert.Contains(publishedEvent.Models, m => m.BikeClass == "Class450" && m.ModelType == "FinishPosition");
    }

    /// <summary>
    /// Test: Handler reloads models in predictor after training
    /// </summary>
    [Fact]
    public async Task Consume_ReloadsModelsInPredictor_AfterTraining()
    {
        // Arrange
        var command = new TrainModelsCommand(DateTimeOffset.UtcNow);

        _mockConsumeContext.Message.Returns(command);
        _mockConsumeContext.MessageId.Returns(Guid.NewGuid());
        _mockConsumeContext.CorrelationId.Returns(Guid.NewGuid());

        SetupSuccessfulModelTraining();

        var handler = CreateHandler();

        // Act
        await handler.Consume(_mockConsumeContext);

        // Assert - Verify ReloadModels was called
        _mockRiderPredictor.Received(1).ReloadModels();
    }

    private void SetupSuccessfulModelTraining()
    {
        _mockModelTrainer.TrainQualificationModelAsync(
            BikeClass.Class250,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateTrainedModelResult(BikeClass.Class250, "Qualification"));

        _mockModelTrainer.TrainFinishPositionModelAsync(
            BikeClass.Class250,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateTrainedModelResult(BikeClass.Class250, "FinishPosition"));

        _mockModelTrainer.TrainQualificationModelAsync(
            BikeClass.Class450,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateTrainedModelResult(BikeClass.Class450, "Qualification"));

        _mockModelTrainer.TrainFinishPositionModelAsync(
            BikeClass.Class450,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateTrainedModelResult(BikeClass.Class450, "FinishPosition"));
    }

    private static TrainedModelResult CreateTrainedModelResult(BikeClass bikeClass, string modelType)
    {
        return new TrainedModelResult(
            Version: "v1.0.0",
            BikeClass: bikeClass.ToString(),
            ModelType: modelType,
            TrainedAt: DateTimeOffset.UtcNow,
            TrainingSamples: 500,
            RSquared: 0.65,
            MeanAbsoluteError: 3.5,
            RootMeanSquaredError: 4.2,
            ModelPath: $"./TrainedModels/{bikeClass}_{modelType}.zip",
            ValidationAccuracy: modelType == "Qualification" ? 0.85 : 0.0);
    }
}
