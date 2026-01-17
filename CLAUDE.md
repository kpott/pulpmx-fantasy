# Project Guidelines for Claude

## About This Application

PulpMX Fantasy is a **decision-support tool for supercross/motocross fantasy sports players**. It helps players optimize their team selections in the PulpMX Fantasy game by providing machine learning-powered predictions of rider performance and calculating optimal team compositions that maximize expected fantasy points.

### Key Features

- **Fantasy Point Predictions**: ML pipeline predicting rider qualification probability, finish position, and expected fantasy points per event
- **Optimal Team Generation**: Algorithmic optimizer selecting the best 8-rider roster while enforcing constraints (4 riders per class, All-Star restrictions, consecutive pick rules)
- **Event Data Sync**: Fetches live event data from PulpMX Fantasy API including rider handicaps and qualifying results
- **Historical Import & Model Training**: Imports past race results and trains separate ML models for Supercross and Motocross series
- **Real-time Updates**: SignalR-powered command status and progress tracking

### Target Users

Competitive fantasy players who want data-driven team selections. The app addresses pain points like analyzing 80+ riders per event, complex scoring rules with handicaps, and tracking consecutive pick restrictions.

### Tech Stack

- ASP.NET Core Web + Worker Service
- PostgreSQL with dual-schema CQRS (public + read_model)
- MassTransit + RabbitMQ for async messaging
- ML.NET for predictions
- SignalR for real-time updates

---

## Development Practices

### Test-Driven Development (TDD)
- Write tests before implementation code
- Red-Green-Refactor cycle: failing test → make it pass → refactor
- Every feature should have corresponding unit tests

### Commit Strategy
- Use **Skill(commit-commands:commit)** for all commits
- Make small, focused commits
- Each commit should represent a single logical change
- Write descriptive commit messages explaining the "why"

## Coding Standards

### Date/Time Handling
- **Always use `DateTimeOffset` instead of `DateTime`** throughout the entire application
- This applies to: entities, DTOs, contracts, APIs, and database columns
- `DateTimeOffset` preserves timezone information and avoids ambiguity

### MassTransit Conventions
- Use the MassTransit skill (`.claude/skills/masstransit.md`) for messaging patterns
- Consumer naming: use `CommandConsumer` or `EventConsumer` suffix (not `CommandHandler`)
- Contracts should use `DateTimeOffset` for all timestamps
- `CorrelationId` is part of MassTransit message headers - do not include it in message contracts

### CQRS Assembly Boundaries
The project enforces CQRS boundaries at the **assembly level** to prevent accidental violations:

**Web Project (read-only + send commands):**
- References: `Contracts`, `ReadModel`, `Messaging`
- **Cannot reference**: `Infrastructure`, `Application`
- Uses `ReadDbContext` only (read_model schema)
- Sends commands via `IBus.Send()`

**Worker Project (full access):**
- References: `Application`, `Infrastructure`, `ReadModel`, `Messaging`
- Processes commands and publishes events
- Writes to both `public` and `read_model` schemas

**Key Projects:**
- `PulpMXFantasy.ReadModel` - ReadDbContext and read model configurations (Web-only)
- `PulpMXFantasy.Messaging` - Shared MassTransit configuration (EndpointConventions)
- `PulpMXFantasy.Contracts` - Commands, Events, ReadModels, shared interfaces

**Adding New Read Models:**
1. Add record to `PulpMXFantasy.Contracts/ReadModels/`
2. Add configuration to `PulpMXFantasy.ReadModel/Data/Configurations/`
3. Add DbSet to `ReadDbContext`
4. Update `IReadModelUpdater` interface and implementation
5. Have Worker populate the read model when relevant events occur

### Exception Handling Strategy

The project uses a deliberate exception handling strategy to provide user feedback via SignalR while allowing MassTransit retries for transient failures.

**Command Consumers (TrainModels, SyncNextEvent, ImportEvents):**
- Catch **transient exceptions** (network, timeout) and **RETHROW** to trigger MassTransit retry
- Catch **non-transient exceptions**, publish `CommandFailedEvent`, do **NOT** rethrow
- Rationale: Transient failures should retry automatically; permanent failures notify users immediately

**Transient Exceptions (rethrow for retry):**
- `HttpRequestException` with status 429 (Too Many Requests), 502, 503, 504
- `TaskCanceledException` caused by timeout
- Database connection failures

**Non-Transient Exceptions (publish failure, don't rethrow):**
- Validation errors
- Business logic failures
- Not found errors
- Unknown/unexpected exceptions (fail fast, notify user)

**Event Consumers (ModelsTrainedEvent):**
- Log errors and continue (fire-and-forget semantics)
- Return empty collections on failure
- Do NOT rethrow - events are eventually consistent

**Pattern for Command Consumers:**
```csharp
try
{
    // Command processing
    await context.Publish(new CommandCompletedEvent(...));
}
catch (HttpRequestException ex) when (IsTransient(ex))
{
    _logger.LogWarning(ex, "Transient failure, will retry");
    throw; // Rethrow for MassTransit retry
}
catch (Exception ex)
{
    _logger.LogError(ex, "Permanent failure");
    await context.Publish(new CommandFailedEvent(...));
    // Do NOT rethrow
}
```
