# PulpMX Fantasy - Technical Architecture

**Version:** 1.0
**Last Updated:** January 11, 2026

This document covers the technical architecture of PulpMX Fantasy, including CQRS patterns, database schema, messaging, and ML prediction details.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Project Structure](#project-structure)
3. [Commands and Events](#commands-and-events)
4. [MassTransit Configuration](#masstransit-configuration)
5. [Database Schema](#database-schema)
6. [ML Prediction System](#ml-prediction-system)

---

## Architecture Overview

PulpMX Fantasy uses a **CQRS (Command Query Responsibility Segregation)** architecture with **Event-Driven** messaging powered by **MassTransit + RabbitMQ**.

```
                           ARCHITECTURE DIAGRAM

  ┌─────────────────┐                         ┌─────────────────────────┐
  │   Web UI        │                         │   Worker Service        │
  │   (Read Only)   │                         │   (Business Logic)      │
  │                 │    Send Commands        │                         │
  │  AdminController├────────────────────────►│  Command Handlers       │
  │  Predictions    │                         │  - SyncNextEvent        │
  │  Controller     │    ┌────────────┐       │  - ImportEvents         │
  │                 │    │            │       │  - TrainModels          │
  │  Query Read     │◄───┤  RabbitMQ  │◄──────┤                         │
  │  Models         │    │  Message   │       │  Event Handlers         │
  │                 │    │  Broker    │       │  - ModelsTrainedHandler │
  └────────┬────────┘    │            │       │                         │
           │             └────────────┘       └───────────┬─────────────┘
           │                                              │
           │                                              │
           ▼                                              ▼
  ┌─────────────────────────────────────────────────────────────────────┐
  │                        PostgreSQL Database                          │
  │  ┌─────────────────────────┐    ┌──────────────────────────────┐   │
  │  │  public schema          │    │  read_model schema           │   │
  │  │  (Write Models)         │    │  (Read Models)               │   │
  │  │                         │    │                              │   │
  │  │  - series               │    │  - event_predictions        │   │
  │  │  - events               │    │  - events                   │   │
  │  │  - riders               │    │  - command_status           │   │
  │  │  - event_riders         │    │  - model_metadata           │   │
  │  │  - teams                │    │                              │   │
  │  │  - team_selections      │    │                              │   │
  │  └─────────────────────────┘    └──────────────────────────────┘   │
  └─────────────────────────────────────────────────────────────────────┘
```

### Architecture Benefits

| Benefit | Description |
|---------|-------------|
| **Non-Blocking HTTP** | Long-running operations (model training: 60-300s) don't block web requests |
| **Scalability** | Worker service can scale independently from web UI |
| **Resilience** | Failed commands are retried automatically via RabbitMQ |
| **Separation of Concerns** | Web UI only displays data; Worker handles all business logic |
| **Event-Driven Pipeline** | Predictions auto-generate after models train |

### Assembly Boundary Enforcement

The project enforces CQRS boundaries at the **assembly level** - Web UI cannot access Infrastructure (ML services, API clients, write operations).

```
                    ┌─────────────┐
                    │   Domain    │
                    └──────┬──────┘
                           │
                    ┌──────▼──────┐
                    │  Contracts  │
                    └──────┬──────┘
                           │
         ┌─────────────────┼─────────────────┐
         │                 │                 │
   ┌─────▼─────┐    ┌──────▼──────┐   ┌──────▼──────┐
   │ Application│    │  ReadModel  │   │  Messaging  │
   └─────┬─────┘    └──────┬──────┘   └──────┬──────┘
         │                 │                 │
   ┌─────▼─────┐          │                 │
   │Infrastructure│        │                 │
   └─────┬─────┘          │                 │
         │                 │                 │
         │    ┌────────────┼─────────────────┤
         │    │            │                 │
   ┌─────▼────▼────┐  ┌────▼─────────────────▼───┐
   │    Worker     │  │           Web            │
   │ (full access) │  │ (read-only + commands)   │
   └───────────────┘  └──────────────────────────┘
```

**Web Project References (read-only access):**
```xml
<ItemGroup>
  <ProjectReference Include="PulpMXFantasy.Contracts" />
  <ProjectReference Include="PulpMXFantasy.ReadModel" />
  <ProjectReference Include="PulpMXFantasy.Messaging" />
  <!-- NO Infrastructure reference - enforced at compile time -->
</ItemGroup>
```

---

## Project Structure

```
PulpMXFantasy/
├── docker-compose.yml                    # Web + Worker + PostgreSQL + RabbitMQ
├── src/
│   ├── PulpMXFantasy.Domain/            # Business entities, enums, value objects
│   │   ├── Entities/                    # EventRider, Rider, Event, Series, Team
│   │   ├── Enums/                       # BikeClass, SeriesType, EventFormat
│   │   ├── Abstractions/                # IRiderPredictor interface
│   │   └── Services/                    # ScoringCalculator (domain logic)
│   │
│   ├── PulpMXFantasy.Contracts/         # Shared message contracts
│   │   ├── Commands/                    # SyncNextEventCommand, ImportEventsCommand, TrainModelsCommand
│   │   ├── Events/                      # EventSyncedEvent, ModelsTrainedEvent, PredictionsGeneratedEvent
│   │   ├── ReadModels/                  # EventPredictionReadModel, EventReadModel, CommandStatusReadModel
│   │   └── Interfaces/                  # ICommandStatusService
│   │
│   ├── PulpMXFantasy.Application/       # Command & Event Handlers
│   │   ├── Consumers/                   # MassTransit consumers
│   │   └── Interfaces/                  # IPredictionService, ITeamOptimizer
│   │
│   ├── PulpMXFantasy.ReadModel/         # Web-only database access
│   │   ├── ReadDbContext.cs             # Read operations (read_model schema)
│   │   ├── Data/Configurations/         # Entity configurations
│   │   └── Services/                    # CommandStatusService
│   │
│   ├── PulpMXFantasy.Messaging/         # Shared MassTransit configuration
│   │   ├── EndpointConventions.cs       # All command endpoint mappings
│   │   └── DependencyInjection.cs       # AddMessagingSendOnly(), AddMessagingWithConsumers()
│   │
│   ├── PulpMXFantasy.Infrastructure/    # External concerns (Worker-only)
│   │   ├── Data/ApplicationDbContext.cs # Write operations (public schema)
│   │   ├── MachineLearning/             # ModelTrainer, MultiStagePredictor
│   │   ├── ExternalApi/                 # PulpMXApiClient
│   │   └── Optimization/                # TeamOptimizerService
│   │
│   ├── PulpMXFantasy.Worker/            # Background processing service
│   │   ├── Program.cs                   # MassTransit consumer host
│   │   └── Consumers/                   # Thin wrapper consumers
│   │
│   └── PulpMXFantasy.Web/               # Presentation layer (reads + commands)
│       ├── Controllers/                 # HomeController, AdminController, PredictionsController
│       └── Views/                       # Razor templates
│
└── tests/
    ├── PulpMXFantasy.Application.Tests/ # Consumer unit tests
    └── PulpMXFantasy.Infrastructure.Tests/
```

---

## Commands and Events

### The Golden Rule: Send vs Publish

| Aspect | Commands | Events |
|--------|----------|--------|
| **Purpose** | Request an action | Notify that something happened |
| **Semantics** | Imperative ("Do this") | Past tense ("This happened") |
| **Consumers** | Exactly ONE | Zero or MORE |
| **MassTransit API** | `IBus.Send(command)` | `IPublishEndpoint.Publish(event)` |

**CRITICAL:**
- Use `IBus.Send()` for commands (NOT Publish)
- Use `IPublishEndpoint.Publish()` for events (NOT Send)

### Command: SyncNextEventCommand

Sync the next upcoming event from PulpMX API.

```csharp
public record SyncNextEventCommand(
    DateTimeOffset Timestamp
);
```

**Typical Duration:** 2-5 seconds

### Command: ImportEventsCommand

Import multiple historical events by slug list.

```csharp
public record ImportEventsCommand(
    IReadOnlyList<string> EventSlugs,
    DateTimeOffset Timestamp
);
```

**Typical Duration:** 5-30+ seconds

### Command: TrainModelsCommand

Train all 4 ML models (multi-stage pipeline).

```csharp
public record TrainModelsCommand(
    DateTimeOffset Timestamp,
    bool Force = false
);
```

**Typical Duration:** 60-300 seconds

### Event: ModelsTrainedEvent

Published after successful model training.

```csharp
public record ModelsTrainedEvent(
    DateTimeOffset TrainedAt,
    List<ModelMetadata> Models,
    int TotalTrainingSamples
);
```

**Subscribers:** `ModelsTrainedEventConsumer` - Automatically generates predictions

---

## MassTransit Configuration

### Key Patterns

1. **Use `DateTimeOffset` for all timestamps** - Never use `DateTime`
2. **CorrelationId is in headers, not contracts** - MassTransit handles this automatically
3. **Consumer naming convention**: `{Name}CommandConsumer` or `{Name}EventConsumer`

### Worker Service (with Consumers)

```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<SyncNextEventConsumer>();
    x.AddConsumer<ImportEventsConsumer>();
    x.AddConsumer<TrainModelsConsumer>();
    x.AddConsumer<ModelsTrainedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h => { ... });
        cfg.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
        cfg.ConfigureEndpoints(context);
    });
});
```

### Web UI (Send-Only)

```csharp
builder.Services.AddMassTransit(x =>
{
    // No consumers - Web UI only sends commands
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq", "/", h => { ... });
    });
});
```

---

## Database Schema

### Schema Overview

```
PostgreSQL Database: pulpmx_fantasy
├── public schema (Write Models)
│   ├── series
│   ├── events
│   ├── riders
│   ├── event_riders
│   ├── teams
│   └── team_selections
│
└── read_model schema (Read Models)
    ├── events
    ├── event_predictions
    ├── command_status
    └── model_metadata
```

### Entity Relationship Diagram

```
SERIES ||--o{ EVENTS : "has many"
EVENTS ||--o{ EVENT_RIDERS : "has many"
RIDERS ||--o{ EVENT_RIDERS : "participates in"
EVENTS ||--o{ TEAMS : "has many"
TEAMS ||--o{ TEAM_SELECTIONS : "has exactly 8"
EVENT_RIDERS ||--o{ TEAM_SELECTIONS : "selected by"
```

### Key Tables

#### public.event_riders (Most Critical)

Event-specific rider data including handicaps, results, and fantasy points.

| Column | Type | Description |
|--------|------|-------------|
| `id` | UUID | Primary key |
| `event_id` | UUID | FK to events |
| `rider_id` | UUID | FK to riders |
| `bike_class` | VARCHAR | Class250 or Class450 |
| `handicap` | INTEGER | Handicap adjustment (-6 to +19) |
| `is_all_star` | BOOLEAN | All-Star status (no doubling) |
| `is_injured` | BOOLEAN | Injury status |
| `finish_position` | INTEGER | Actual race finish (null until race) |
| `fantasy_points` | INTEGER | Calculated fantasy points |

#### read_model.event_predictions

Materialized predictions for UI display.

| Column | Type | Description |
|--------|------|-------------|
| `id` | UUID | Primary key |
| `event_id` | UUID | Event identifier |
| `rider_id` | UUID | Rider identifier |
| `bike_class` | VARCHAR | Class250 or Class450 |
| `expected_points` | FLOAT | Risk-adjusted predicted points |
| `points_if_qualifies` | FLOAT | Points assuming rider makes main |
| `predicted_finish` | INTEGER | Force-ranked finish position (1-22) |
| `confidence` | FLOAT | Prediction confidence (0-1) |

#### read_model.events

Denormalized event data for Web UI (avoids querying write model).

| Column | Type | Description |
|--------|------|-------------|
| `id` | UUID | Primary key |
| `name` | VARCHAR | Event name |
| `slug` | VARCHAR | API identifier |
| `event_date` | TIMESTAMPTZ | Event date |
| `lockout_time` | TIMESTAMPTZ | Deadline for team changes |
| `is_completed` | BOOLEAN | Whether event has occurred |

---

## ML Prediction System

### RiderPrediction Record

The `RiderPrediction` record (defined in `IRiderPredictor.cs`) contains all prediction data:

```csharp
public record RiderPrediction(
    Guid RiderId,
    BikeClass BikeClass,
    bool IsAllStar,
    float ExpectedPoints,      // Qualification probability × PointsIfQualifies
    float PointsIfQualifies,   // Fantasy points assuming rider makes main event
    int? PredictedFinish,      // Force-ranked position 1-22, null if DNQ predicted
    float LowerBound,          // 80% confidence interval lower bound
    float UpperBound,          // 80% confidence interval upper bound
    float Confidence           // Overall prediction confidence (0.0 - 1.0)
);
```

### ExpectedPoints vs PointsIfQualifies

These two fields serve different purposes:

| Field | Formula | Purpose |
|-------|---------|---------|
| **PointsIfQualifies** | `FantasyPoints(predictedFinish, handicap, isAllStar)` | Shows potential upside if rider makes the main event |
| **ExpectedPoints** | `P(qualification) × PointsIfQualifies` | Risk-adjusted expected value accounting for DNQ probability |

**Example:**
- Rider predicted to finish 5th with handicap +3 (adjusted 2nd = 22 pts, doubled = 44 pts)
- Qualification probability: 75%
- **PointsIfQualifies:** 44 points
- **ExpectedPoints:** 0.75 × 44 = 33 points

**UI Display:**
- "Pts if Qual" column shows `PointsIfQualifies` (what you could get)
- "Expected Pts" column shows `ExpectedPoints` (risk-adjusted value)

Users can use both values:
- **Conservative strategy:** Sort by ExpectedPoints (accounts for DNQ risk)
- **Aggressive strategy:** Sort by PointsIfQualifies (maximum upside)

### Force-Ranking in PredictBatch

**Problem:** Raw ML predictions can produce duplicate finish positions (e.g., three riders predicted to finish 5th).

**Solution:** The `PredictBatch` method in `MultiStagePredictor` applies force-ranking to ensure unique positions 1-22 within each bike class.

**Algorithm:**
```
1. Generate raw predictions for all riders
2. For each bike class (250, 450):
   a. Separate riders into qualifiers (PredictedFinish != null) and DNQs
   b. Sort qualifiers by:
      - Primary: ML-predicted finish position (ascending)
      - Tie-breaker: ExpectedPoints (descending)
   c. Assign force-ranked positions 1, 2, 3... up to 22
   d. DNQ riders keep PredictedFinish = null
3. Return all predictions with force-ranked positions
```

**Example:**
```
Raw Predictions (450 class):
  Rider A: predicted 3rd, 45 pts
  Rider B: predicted 3rd, 42 pts  <- tie
  Rider C: predicted 5th, 38 pts

After Force-Ranking:
  Rider A: position 1 (was 3rd, higher points wins tie)
  Rider B: position 2 (was 3rd)
  Rider C: position 3 (was 5th)
```

**Why Force-Ranking Matters:**
- Supercross main events have exactly 22 unique positions
- UI displays predicted finish order clearly (1st, 2nd, 3rd...)
- Team optimizer can use unique positions for strategy
- Prevents confusing duplicate position predictions

**Location:** `src/PulpMXFantasy.Infrastructure/MachineLearning/MultiStagePredictor.cs:PredictBatch()`

### LockoutTime

The `LockoutTime` field on events indicates when team picks can no longer be changed:

- Typically set to the race start time (main event)
- After lockout, predictions page shows "Locked" status
- Model training is disabled during lockout to protect existing predictions
- Web UI checks `LockoutTime <= DateTimeOffset.UtcNow` to determine lock status

**Location:** `read_model.events.lockout_time` column, queried by `AdminController.Index()` and `PredictionsController.Index()`

---

## Document History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2026-01-11 | Combined architecture-cqrs.md and architecture-database.md; Added ML prediction details (PointsIfQualifies, force-ranking, LockoutTime) |
