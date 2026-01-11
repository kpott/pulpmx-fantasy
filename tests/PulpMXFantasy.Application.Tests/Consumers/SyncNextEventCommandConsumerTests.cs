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
/// Unit tests for SyncNextEventCommandConsumer following TDD methodology.
/// </summary>
/// <remarks>
/// WHAT THIS TESTS:
/// ================
/// 1. Handler calls EventSyncService.SyncNextEventAsync()
/// 2. Handler updates command status to Running, then Completed
/// 3. Handler publishes EventSyncedEvent on success
/// 4. Error handling updates status to Failed
///
/// TDD APPROACH:
/// =============
/// These tests were written BEFORE the handler implementation.
/// Each test defines the expected behavior that the handler must implement.
/// </remarks>
public class SyncNextEventCommandConsumerTests
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ICommandStatusService _commandStatusService;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<SyncNextEventCommandConsumer> _logger;
    private readonly ConsumeContext<SyncNextEventCommand> _consumeContext;
    private readonly SyncNextEventCommandConsumer _handler;

    public SyncNextEventCommandConsumerTests()
    {
        // Create mocks using interfaces
        _eventSyncService = Substitute.For<IEventSyncService>();
        _commandStatusService = Substitute.For<ICommandStatusService>();
        _publishEndpoint = Substitute.For<IPublishEndpoint>();
        _logger = Substitute.For<ILogger<SyncNextEventCommandConsumer>>();
        _consumeContext = Substitute.For<ConsumeContext<SyncNextEventCommand>>();

        // Create handler under test
        _handler = new SyncNextEventCommandConsumer(
            _eventSyncService,
            _commandStatusService,
            _publishEndpoint,
            _logger);
    }

    [Fact]
    public async Task Consume_CallsEventSyncServiceToSyncNextEvent()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert
        await _eventSyncService.Received(1).SyncNextEventAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UpdatesStatusToRunningThenCompleted()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(correlationId);

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - Verify status lifecycle
        // 1. First, command status should be created (Pending)
        await _commandStatusService.Received(1).CreateAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            "SyncNextEvent",
            Arg.Any<CancellationToken>());

        // 2. Then, status should be updated to Running
        await _commandStatusService.Received(1).UpdateProgressAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        // 3. Finally, status should be marked Completed
        await _commandStatusService.Received(1).CompleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<object?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PublishesEventSyncedEventOnSuccess()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - EventSyncedEvent should be published
        await _publishEndpoint.Received(1).Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishEventWhenSyncReturnsFalse()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        // Sync returns false (no event to sync)
        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - No EventSyncedEvent should be published
        await _publishEndpoint.DidNotReceive().Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());

        // But status should still be completed (just no event synced)
        await _commandStatusService.Received(1).CompleteAsync(
            Arg.Any<Guid>(),
            Arg.Any<object?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UpdatesStatusToFailedOnException()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(correlationId);

        var expectedException = new InvalidOperationException("Test error during sync");
        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(expectedException);

        // Act & Assert - Handler should NOT rethrow (to prevent retry storm)
        // but should mark status as Failed
        await _handler.Consume(_consumeContext);

        // Verify status was marked as Failed
        await _commandStatusService.Received(1).FailAsync(
            Arg.Any<Guid>(),
            Arg.Is<string>(msg => msg.Contains("Test error during sync")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishEventOnError()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Sync failed"));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - No event should be published
        await _publishEndpoint.DidNotReceive().Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_UsesCorrelationIdFromContext()
    {
        // Arrange
        var command = new SyncNextEventCommand(DateTimeOffset.UtcNow);
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(correlationId);

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - CorrelationId should be passed to status service
        await _commandStatusService.Received(1).CreateAsync(
            Arg.Any<Guid>(),
            correlationId,  // Should use the correlation ID from context
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
