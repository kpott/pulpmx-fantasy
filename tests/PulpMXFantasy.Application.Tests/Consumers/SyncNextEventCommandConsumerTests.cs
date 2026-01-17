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
/// Unit tests for SyncNextEventCommandConsumer following TDD methodology.
/// </summary>
/// <remarks>
/// WHAT THIS TESTS:
/// ================
/// 1. Handler calls EventSyncService.SyncNextEventAsync()
/// 2. Handler publishes CommandStartedEvent, CommandProgressUpdatedEvent
/// 3. Handler publishes EventSyncedEvent on success
/// 4. Handler publishes CommandCompletedEvent or CommandFailedEvent
///
/// EVENT-DRIVEN ARCHITECTURE:
/// ==========================
/// The consumer publishes events via context.Publish() instead of calling
/// ICommandStatusService directly. This allows the Web project's consumer
/// to handle status updates and push to SignalR.
/// </remarks>
public class SyncNextEventCommandConsumerTests
{
    private readonly IEventSyncService _eventSyncService;
    private readonly ILogger<SyncNextEventCommandConsumer> _logger;
    private readonly ConsumeContext<SyncNextEventCommand> _consumeContext;
    private readonly SyncNextEventCommandConsumer _handler;

    public SyncNextEventCommandConsumerTests()
    {
        // Create mocks using interfaces
        _eventSyncService = Substitute.For<IEventSyncService>();
        _logger = Substitute.For<ILogger<SyncNextEventCommandConsumer>>();
        _consumeContext = Substitute.For<ConsumeContext<SyncNextEventCommand>>();

        // Create handler under test
        _handler = new SyncNextEventCommandConsumer(
            _eventSyncService,
            _logger);
    }

    [Fact]
    public async Task Consume_CallsEventSyncServiceToSyncNextEvent()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
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
    public async Task Consume_PublishesCommandStartedEvent()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(correlationId);

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - CommandStartedEvent should be published
        await _consumeContext.Received(1).Publish(
            Arg.Is<CommandStartedEvent>(e =>
                e.CommandId == messageId &&
                e.CommandType == "SyncNextEvent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PublishesProgressUpdateEvent()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - CommandProgressUpdatedEvent should be published
        await _consumeContext.Received().Publish(
            Arg.Any<CommandProgressUpdatedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PublishesEventSyncedEventOnSuccess()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - EventSyncedEvent should be published
        await _consumeContext.Received(1).Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PublishesCommandCompletedEventOnSuccess()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var messageId = Guid.NewGuid();
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - CommandCompletedEvent should be published
        await _consumeContext.Received(1).Publish(
            Arg.Is<CommandCompletedEvent>(e => e.CommandId == messageId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishEventSyncedWhenSyncReturnsFalse()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        // Sync returns false (no event to sync)
        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - No EventSyncedEvent should be published
        await _consumeContext.DidNotReceive().Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());

        // But CommandCompletedEvent should still be published
        await _consumeContext.Received(1).Publish(
            Arg.Any<CommandCompletedEvent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PublishesCommandFailedEventOnException()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        var messageId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(messageId);
        _consumeContext.CorrelationId.Returns(correlationId);

        var expectedException = new InvalidOperationException("Test error during sync");
        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(expectedException);

        // Act & Assert - Handler should NOT rethrow (to prevent retry storm)
        await _handler.Consume(_consumeContext);

        // Verify CommandFailedEvent was published
        await _consumeContext.Received(1).Publish(
            Arg.Is<CommandFailedEvent>(e =>
                e.CommandId == messageId &&
                e.ErrorMessage.Contains("Test error during sync")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DoesNotPublishEventSyncedOnError()
    {
        // Arrange
        var command = new SyncNextEventCommand(Guid.NewGuid(), DateTimeOffset.UtcNow);
        _consumeContext.Message.Returns(command);
        _consumeContext.MessageId.Returns(Guid.NewGuid());
        _consumeContext.CorrelationId.Returns(Guid.NewGuid());

        _eventSyncService.SyncNextEventAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Sync failed"));

        // Act
        await _handler.Consume(_consumeContext);

        // Assert - No EventSyncedEvent should be published
        await _consumeContext.DidNotReceive().Publish(
            Arg.Any<EventSyncedEvent>(),
            Arg.Any<CancellationToken>());
    }
}
