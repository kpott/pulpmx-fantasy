# Project Guidelines for Claude

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
