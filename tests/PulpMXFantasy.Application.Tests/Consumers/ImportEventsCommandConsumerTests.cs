using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;

namespace PulpMXFantasy.Application.Tests.Consumers;

/// <summary>
/// Unit tests for ImportEventsCommandConsumer following TDD methodology.
/// </summary>
/// <remarks>
/// WHAT THIS TESTS:
/// ================
/// 1. Validation - Empty slug list rejected
/// 2. Sequential Processing - Events processed one by one
/// 3. Progress Tracking - Publishes CommandProgressUpdatedEvent
/// 4. Event Publishing - Publishes EventSyncedEvent for each success
/// 5. Error Resilience - One failure doesn't stop processing
///
/// EVENT-DRIVEN ARCHITECTURE:
/// ==========================
/// The consumer publishes events via context.Publish() instead of calling
/// ICommandStatusService directly. This allows the Web project's consumer
/// to handle status updates and push to SignalR.
/// </remarks>
public class ImportEventsCommandConsumerTests
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ILogger<ImportEventsCommandConsumer> _logger;
    private readonly ConsumeContext<ImportEventsCommand> _consumeContext;
    private readonly ImportEventsCommandConsumer _handler;

    public ImportEventsCommandConsumerTests()
    {
        // Create mocks using interfaces
        _eventSyncService = Substitute.For<IEventSyncService>();
        _logger = Substitute.For<ILogger<ImportEventsCommandConsumer>>();
        _consumeContext = Substitute.For<ConsumeContext<ImportEventsCommand>>();

        // Create handler under test
        _handler = new ImportEventsCommandConsumer(
            _eventSyncService,
            _logger);
    }

    [Fact]
    public async Task Consume_EmptySlugList_DoesNotProcess()
    {
        // Arrange
        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: new List<string>(),
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should not call sync service
        await _eventSyncService.DidNotReceive().SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SingleSlug_ProcessesEventAndPublishes()
    {
        // Arrange
        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: new List<string> { "anaheim-1-2025-sx" },
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should call sync service once
        await _eventSyncService.Received(1).SyncHistoricalEventAsync(
            "anaheim-1-2025-sx",
            Arg.Any<CancellationToken>());

        // Assert - Should publish EventSyncedEvent via context
        await _consumeContext.Received(1).Publish(
            Arg.Is<EventSyncedEvent>(e => e.EventSlug == "anaheim-1-2025-sx"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MultipleSlugs_ProcessesSequentially()
    {
        // Arrange
        var slugs = new List<string>
        {
            "anaheim-1-2025-sx",
            "san-diego-2025-sx",
            "glendale-2025-sx"
        };

        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        // Track call order
        var callOrder = new List<string>();
        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callOrder.Add(callInfo.Arg<string>());
                return true;
            });

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should process in order
        Assert.Equal(slugs, callOrder);

        // Assert - Should call sync service 3 times
        await _eventSyncService.Received(3).SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PublishesCommandStartedEvent()
    {
        // Arrange
        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: new List<string> { "test-event" },
            Timestamp: DateTimeOffset.UtcNow);

        var messageId = Guid.NewGuid();
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - CommandStartedEvent should be published
        await _consumeContext.Received(1).Publish(
            Arg.Is<CommandStartedEvent>(e =>
                e.CommandId == messageId &&
                e.CommandType == "ImportEvents"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MultipleSlugs_PublishesProgressUpdates()
    {
        // Arrange
        var slugs = new List<string>
        {
            "event-1",
            "event-2",
            "event-3",
            "event-4"
        };

        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        var progressEvents = new List<CommandProgressUpdatedEvent>();
        _consumeContext.Publish(
            Arg.Do<CommandProgressUpdatedEvent>(e => progressEvents.Add(e)),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should publish progress events
        await _consumeContext.Received().Publish(
            Arg.Any<CommandProgressUpdatedEvent>(),
            Arg.Any<CancellationToken>());

        // Verify progress percentages include expected values
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 25);
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 50);
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 75);
        Assert.Contains(progressEvents, p => p.ProgressPercentage == 100);
    }

    [Fact]
    public async Task Consume_SuccessfulSync_PublishesEventSyncedEvent()
    {
        // Arrange
        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: new List<string> { "test-event-sx" },
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should publish event with correct slug via context
        await _consumeContext.Received(1).Publish(
            Arg.Is<EventSyncedEvent>(e =>
                e.EventSlug == "test-event-sx" &&
                e.EventId != Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_FailedSync_DoesNotPublishEventButContinues()
    {
        // Arrange
        var slugs = new List<string>
        {
            "good-event-1",
            "bad-event",
            "good-event-2"
        };

        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        // First and third succeed, second fails
        _eventSyncService.SyncHistoricalEventAsync(
            "good-event-1",
            Arg.Any<CancellationToken>())
            .Returns(true);

        _eventSyncService.SyncHistoricalEventAsync(
            "bad-event",
            Arg.Any<CancellationToken>())
            .Returns(false);

        _eventSyncService.SyncHistoricalEventAsync(
            "good-event-2",
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should call all three
        await _eventSyncService.Received(3).SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        // Assert - Should only publish 2 EventSyncedEvents (not the failed one)
        await _consumeContext.Received(2).Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());

        // Verify the failed event was NOT published
        await _consumeContext.DidNotReceive().Publish(
            Arg.Is<EventSyncedEvent>(e => e.EventSlug == "bad-event"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_ExceptionOnOneSlug_ContinuesWithRemaining()
    {
        // Arrange
        var slugs = new List<string>
        {
            "event-1",
            "event-throws",
            "event-3"
        };

        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            "event-1",
            Arg.Any<CancellationToken>())
            .Returns(true);

        _eventSyncService.SyncHistoricalEventAsync(
            "event-throws",
            Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("API error"));

        _eventSyncService.SyncHistoricalEventAsync(
            "event-3",
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should call all three despite exception
        await _eventSyncService.Received(3).SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        // Assert - Should publish 2 EventSyncedEvents (skipping the one that threw)
        await _consumeContext.Received(2).Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_CompletesSuccessfully_PublishesCommandCompletedEvent()
    {
        // Arrange
        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: new List<string> { "test-event" },
            Timestamp: DateTimeOffset.UtcNow);

        var messageId = Guid.NewGuid();
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should publish CommandCompletedEvent
        await _consumeContext.Received(1).Publish(
            Arg.Is<CommandCompletedEvent>(e => e.CommandId == messageId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_AllSlugsFail_StillCompletesWithResults()
    {
        // Arrange
        var command = new ImportEventsCommand(
            CommandId: Guid.NewGuid(),
            EventSlugs: new List<string> { "bad-1", "bad-2" },
            Timestamp: DateTimeOffset.UtcNow);

        var messageId = Guid.NewGuid();
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Should still publish CommandCompletedEvent (not fail entirely)
        await _consumeContext.Received(1).Publish(
            Arg.Is<CommandCompletedEvent>(e => e.CommandId == messageId),
            Arg.Any<CancellationToken>());

        // Assert - No EventSyncedEvents published
        await _consumeContext.DidNotReceive().Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }
}
