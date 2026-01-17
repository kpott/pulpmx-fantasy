using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Contracts.Commands;
using PulpMXFantasy.Contracts.Interfaces;
using PulpMXFantasy.ReadModel;

namespace PulpMXFantasy.Web.Controllers;

/// <summary>
/// Admin controller - Data sync, model training, and admin operations.
/// </summary>
/// <remarks>
/// CQRS ARCHITECTURE:
/// ==================
/// This controller ONLY sends commands to the Worker Service via MassTransit.
/// All business logic is handled by consumers in the Worker Service.
///
/// WHY IBus:
/// =========
/// Per Chris Patterson (phatboyg) - use IBus for simplicity in controllers:
/// - IBus.Send() uses convention-based routing to send commands
/// - No need to manually construct endpoint URIs
/// - MassTransit automatically routes to the correct consumer queue
///
/// SECURITY NOTE: In production, add [Authorize(Roles = "Admin")] attribute
/// and implement proper authentication/authorization.
/// </remarks>
public class AdminController : Controller
{
    private readonly IBus _bus;
    private readonly ICommandStatusService _commandStatusService;
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IBus bus,
        ICommandStatusService commandStatusService,
        ReadDbContext readDbContext,
        ILogger<AdminController> logger)
    {
        _bus = bus;
        _commandStatusService = commandStatusService;
        _readDbContext = readDbContext;
        _logger = logger;
    }

    /// <summary>
    /// Admin dashboard.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        // Get recent command statuses to display on dashboard
        var recentStatuses = await _commandStatusService.GetRecentAsync(10);
        ViewBag.RecentStatuses = recentStatuses;

        // Check if predictions are locked for the next upcoming event
        // Only consider future events (past events shouldn't block training)
        var now = DateTimeOffset.UtcNow;
        var nextEvent = await _readDbContext.Events
            .AsNoTracking()
            .Where(e => !e.IsCompleted && e.EventDate >= now.Date)
            .OrderBy(e => e.EventDate)
            .FirstOrDefaultAsync();

        ViewBag.NextEvent = nextEvent;
        ViewBag.IsPredictionsLocked = nextEvent?.LockoutTime.HasValue == true
            && nextEvent.LockoutTime.Value <= now;

        return View();
    }

    /// <summary>
    /// Manually trigger sync of next event from PulpMX API.
    /// </summary>
    /// <remarks>
    /// Sends SyncNextEventCommand to Worker Service.
    /// Returns immediately - command executes asynchronously.
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> SyncNextEvent()
    {
        var commandId = Guid.NewGuid();

        _logger.LogInformation("Sending SyncNextEventCommand {CommandId}", commandId);

        var command = new SyncNextEventCommand(
            Timestamp: DateTimeOffset.UtcNow
        );

        await _bus.Send(command);

        TempData["Info"] = "Sync command submitted. Check status below for progress.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Sync historical event by slug.
    /// </summary>
    /// <remarks>
    /// Sends ImportEventsCommand with single slug to Worker Service.
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> SyncHistoricalEvent(string eventSlug)
    {
        if (string.IsNullOrWhiteSpace(eventSlug))
        {
            TempData["Error"] = "Event slug is required.";
            return RedirectToAction("Index");
        }

        _logger.LogInformation("Sending ImportEventsCommand for slug: {EventSlug}", eventSlug);

        var command = new ImportEventsCommand(
            EventSlugs: [eventSlug.Trim()],
            Timestamp: DateTimeOffset.UtcNow
        );

        await _bus.Send(command);

        TempData["Info"] = $"Import command for '{eventSlug}' submitted. Check status below for progress.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Import a complete season of historical data.
    /// </summary>
    /// <remarks>
    /// Sends ImportEventsCommand with all slugs for the season.
    /// Uses static helper to get known event slugs.
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> ImportSeason(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            TempData["Error"] = "Season selection is required.";
            return RedirectToAction("Index");
        }

        // Get event slugs for the season (static lookup)
        var eventSlugs = GetSeasonEventSlugs(season).ToList();

        if (eventSlugs.Count == 0)
        {
            TempData["Error"] = $"No events found for season: {season}";
            return RedirectToAction("Index");
        }

        _logger.LogInformation(
            "Sending ImportEventsCommand for season {Season} ({Count} events)",
            season,
            eventSlugs.Count);

        var command = new ImportEventsCommand(
            EventSlugs: eventSlugs,
            Timestamp: DateTimeOffset.UtcNow
        );

        await _bus.Send(command);

        TempData["Info"] = $"Import command for {eventSlugs.Count} events from {season} submitted. Check status below for progress.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Import custom list of event slugs.
    /// </summary>
    /// <remarks>
    /// Parses input and sends ImportEventsCommand to Worker Service.
    /// Input parsing is controller responsibility (presentation layer).
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> ImportCustomEvents(string eventSlugs)
    {
        if (string.IsNullOrWhiteSpace(eventSlugs))
        {
            TempData["Error"] = "Event slugs are required.";
            return RedirectToAction("Index");
        }

        // Parse comma-separated or line-separated event slugs (input parsing = presentation layer)
        var slugList = eventSlugs
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (slugList.Count == 0)
        {
            TempData["Error"] = "No valid event slugs provided.";
            return RedirectToAction("Index");
        }

        _logger.LogInformation(
            "Sending ImportEventsCommand for {Count} custom events",
            slugList.Count);

        var command = new ImportEventsCommand(
            EventSlugs: slugList,
            Timestamp: DateTimeOffset.UtcNow
        );

        await _bus.Send(command);

        TempData["Info"] = $"Import command for {slugList.Count} events submitted. Check status below for progress.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Train ML models for fantasy point prediction.
    /// </summary>
    /// <remarks>
    /// Sends TrainModelsCommand to Worker Service.
    /// This is a long-running operation (60-300 seconds).
    /// Worker handles all 4 models sequentially and updates status.
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> TrainModels()
    {
        _logger.LogInformation("Sending TrainModelsCommand");

        var command = new TrainModelsCommand(
            Timestamp: DateTimeOffset.UtcNow,
            Force: false
        );

        await _bus.Send(command);

        TempData["Info"] = "Model training command submitted. This may take several minutes. Check status below for progress.";
        return RedirectToAction("Index");
    }

    /// <summary>
    /// Get command status for polling.
    /// </summary>
    /// <remarks>
    /// Returns JSON status for AJAX polling from UI.
    /// </remarks>
    [HttpGet]
    public async Task<IActionResult> GetCommandStatus(Guid commandId)
    {
        var status = await _commandStatusService.GetByIdAsync(commandId);

        if (status == null)
        {
            return NotFound();
        }

        return Json(new
        {
            status.CommandId,
            status.CommandType,
            status.Status,
            status.ProgressMessage,
            status.ProgressPercentage,
            status.StartedAt,
            status.CompletedAt,
            status.ErrorMessage
        });
    }

    /// <summary>
    /// Gets event slugs for a given season.
    /// </summary>
    /// <remarks>
    /// This is a static lookup - moved from HistoricalDataImportService
    /// so Web doesn't need Infrastructure dependency.
    /// </remarks>
    private static IEnumerable<string> GetSeasonEventSlugs(string season)
    {
        return season.ToLowerInvariant() switch
        {
            "2026-supercross" => new[]
            {
                "anaheim2-sx-26", "sandiego-sx-26", "houston-sx-26"
            },
            "2024-supercross" => new[]
            {
                "anaheim1-sx-24", "sanfrancisco-sx-24", "sandiego-sx-24", "anaheim2-sx-24",
                "detroit-sx-24", "glendale-sx-24", "arlington-sx-24", "daytona-sx-24",
                "birmingham-sx-24", "indianapolis-sx-24", "seattle-sx-24", "stlouis-sx-24",
                "foxborough-sx-24", "nashville-sx-24", "philadelphia-sx-24", "denver-sx-24",
                "saltlakecity-sx-24"
            },
            "2024-motocross" => new[]
            {
                "foxraceway-mx-24", "hangtown-mx-24", "thundervalley-mx-24", "highpoint-mx-24",
                "southwick-mx-24", "redbud-mx-24", "springcreek-mx-24", "washougal-mx-24",
                "unadilla-mx-24", "buddscreek-mx-24", "ironman-mx-24"
            },
            "2024-smx" => new[]
            {
                "charlotte-smx-24", "forthworth-smx-24", "lasvegas-smx-24"
            },
            "2025-supercross" => new[]
            {
                "anaheim-sx-25", "sandiego-sx-25", "anaheim2-sx-25", "glendale-sx-25",
                "tampa-sx-25", "detroit-sx-25", "arlington-sx-25", "daytona-sx-25",
                "indianapolis-sx-25", "birmingham-sx-25", "seattle-sx-25", "foxborough-sx-25",
                "philadelphia-sx-25", "eastrutherford-sx-25", "pittsburgh-sx-25", "denver-sx-25",
                "saltlakecity-sx-25"
            },
            "2025-motocross" => new[]
            {
                "foxraceway-mx-25", "hangtown-mx-25", "thundervalley-mx-25", "highpoint-mx-25",
                "southwick-mx-25", "redbud-mx-25", "springcreek-mx-25", "washougal-mx-25",
                "unadilla-mx-25", "buddscreek-mx-25", "ironman-mx-25"
            },
            "2025-smx" => new[]
            {
                "charlotte-smx-25", "stlouis-smx-25", "lasvegas-smx-25"
            },
            _ => Array.Empty<string>()
        };
    }
}
