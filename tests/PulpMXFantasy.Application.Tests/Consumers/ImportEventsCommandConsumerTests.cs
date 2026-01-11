using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PulpMXFantasy.Application.Consumers;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.Application.Interfaces;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Events;

namespace PulpMXFantasy.Application.Tests.Consumers;

/// <summary>
/// Unit tests for ImportEventsCommandConsumer.
/// </summary>
/// <remarks>
/// TDD TESTS FOR:
/// ==============
/// 1. Validation - Empty slug list rejected
/// 2. Sequential Processing - Events processed one by one
/// 3. Progress Tracking - Updates X/Y complete
/// 4. Event Publishing - Publishes EventSyncedEvent for each success
/// 5. Error Resilience - One failure doesn't stop processing
///
/// MOCKING STRATEGY:
/// =================
/// - IEventSyncService: Mock to control sync success/failure
/// - ICommandStatusService: Mock to verify progress updates
/// - IPublishEndpoint: Mock to verify event publishing
/// - ConsumeContext: Mock to provide command and correlation ID
/// </remarks>
public class ImportEventsCommandConsumerTests
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ICommandStatusService _commandStatusService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<ImportEventsCommandConsumer> _logger;
    private readonly ImportEventsCommandConsumer _handler;

    public ImportEventsCommandConsumerTests()
    {
        _eventSyncService = Substitute.For<IEventSyncService>();
        _commandStatusService = Substitute.For<ICommandStatusService>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _logger = Substitute.For<ILogger<ImportEventsCommandConsumer>>();

        _handler = new ImportEventsCommandConsumer(
            _eventSyncService,
            _commandStatusService,
            _publishEndpoint,
            _logger);
    }

    [Fact]
    public async Task Consume_EmptySlugList_DoesNotProcess()
    {
        // Arrange
        var command = new ImportEventsCommand(
            EventSlugs: new List<string>(),
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

        // Act
        await _handler.Consume(context);

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
            EventSlugs: new List<string> { "anaheim-1-2025-sx" },
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(context);

        // Assert - Should call sync service once
        await _eventSyncService.Received(1).SyncHistoricalEventAsync(
            "anaheim-1-2025-sx",
            Arg.Any<CancellationToken>());

        // Assert - Should publish EventSyncedEvent
        await _publishEndpoint.Received(1).Publish(
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
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

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
        await _handler.Consume(context);

        // Assert - Should process in order
        Assert.Equal(slugs, callOrder);

        // Assert - Should call sync service 3 times
        await _eventSyncService.Received(3).SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MultipleSlugs_UpdatesProgressCorrectly()
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
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        var progressUpdates = new List<(string Message, int Percentage)>();
        _commandStatusService.UpdateProgressAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(callInfo =>
            {
                progressUpdates.Add((callInfo.Arg<string>(), callInfo.ArgAt<int>(2)));
            });

        // Act
        await _handler.Consume(context);

        // Assert - Should update progress for each event
        // After event 1: 25%, After event 2: 50%, After event 3: 75%, After event 4: 100%
        Assert.Contains(progressUpdates, p => p.Percentage == 25);
        Assert.Contains(progressUpdates, p => p.Percentage == 50);
        Assert.Contains(progressUpdates, p => p.Percentage == 75);
        Assert.Contains(progressUpdates, p => p.Percentage == 100);
    }

    [Fact]
    public async Task Consume_SuccessfulSync_PublishesEventSyncedEvent()
    {
        // Arrange
        var command = new ImportEventsCommand(
            EventSlugs: new List<string> { "test-event-sx" },
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(context);

        // Assert - Should publish event with correct slug
        await _publishEndpoint.Received(1).Publish(
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
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

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
        await _handler.Consume(context);

        // Assert - Should call all three
        await _eventSyncService.Received(3).SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        // Assert - Should only publish 2 events (not the failed one)
        await _publishEndpoint.Received(2).Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());

        // Verify the failed event was NOT published
        await _publishEndpoint.DidNotReceive().Publish(
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
            EventSlugs: slugs,
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

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
        await _handler.Consume(context);

        // Assert - Should call all three despite exception
        await _eventSyncService.Received(3).SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        // Assert - Should publish 2 events (skipping the one that threw)
        await _publishEndpoint.Received(2).Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_CompletesSuccessfully_CallsCommandStatusComplete()
    {
        // Arrange
        var command = new ImportEventsCommand(
            EventSlugs: new List<string> { "test-event" },
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        await _handler.Consume(context);

        // Assert - Should call CompleteAsync
        await _commandStatusService.Received(1).CompleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<object?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_AllSlugsFail_StillCompletesWithResults()
    {
        // Arrange
        var command = new ImportEventsCommand(
            EventSlugs: new List<string> { "bad-1", "bad-2" },
            Timestamp: DateTimeOffset.UtcNow);

        var context = CreateConsumeContext(command);

        _eventSyncService.SyncHistoricalEventAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        await _handler.Consume(context);

        // Assert - Should still complete (not fail entirely)
        await _commandStatusService.Received(1).CompleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<object?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        // Assert - No events published
        await _publishEndpoint.DidNotReceive().Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates a mock ConsumeContext for testing.
    /// </summary>
    private ConsumeContext<ImportEventsCommand> CreateConsumeContext(ImportEventsCommand command)
    {
        var context = Substitute.For<ConsumeContext<ImportEventsCommand>>();
        context.Message.Returns(command);
        context.CorrelationId.Returns(Guid.NewGuid());
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}
