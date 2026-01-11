using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.ReadModel;
using PulpMXFantasy.Web.Models;

namespace PulpMXFantasy.Web.Controllers;

/// <summary>
/// Predictions controller - Display ML predictions from read model.
/// </summary>
/// <remarks>
/// CQRS Pattern: This controller only READS from the read_model schema.
/// - Predictions are pre-computed by Worker service after model training
/// - Events are synced by Worker and populated in read_model.events
/// - NO access to write models (enforced at assembly level)
/// - NO ML inference happens here - just database queries
/// </remarks>
public class PredictionsController : Controller
{
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<PredictionsController> _logger;

    public PredictionsController(
        ReadDbContext readDbContext,
        ILogger<PredictionsController> logger)
    {
        _readDbContext = readDbContext;
        _logger = logger;
    }

    /// <summary>
    /// Display predictions for next upcoming event.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        try
        {
            // Find next upcoming event from read model
            var nextEvent = await _readDbContext.Events
                .AsNoTracking()
                .Where(e => !e.IsCompleted && e.EventDate >= DateTimeOffset.UtcNow)
                .OrderBy(e => e.EventDate)
                .FirstOrDefaultAsync();

            if (nextEvent == null)
            {
                ViewBag.Message = "No upcoming events found. Events will appear after syncing from the Admin panel.";
                return View(new PredictionsViewModel());
            }

            // Query predictions from read model (no ML inference - just DB query)
            var predictions = await _readDbContext.EventPredictions
                .AsNoTracking()
                .Where(p => p.EventId == nextEvent.Id)
                .OrderByDescending(p => p.ExpectedPoints)
                .ToListAsync();

            _logger.LogInformation(
                "Loaded {PredictionCount} predictions for event {EventName} from read model",
                predictions.Count, nextEvent.Name);

            var model = new PredictionsViewModel
            {
                Event = nextEvent,
                Predictions = predictions
            };

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading predictions from read model");
            ViewBag.Error = "Error loading predictions. Please try again.";
            return View(new PredictionsViewModel());
        }
    }

    /// <summary>
    /// Display predictions for specific event by ID.
    /// </summary>
    [HttpGet("predictions/event/{eventId}")]
    public async Task<IActionResult> Event(Guid eventId)
    {
        try
        {
            // Load event from read model
            var eventEntity = await _readDbContext.Events
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (eventEntity == null)
            {
                return NotFound("Event not found.");
            }

            // Query predictions from read model
            var predictions = await _readDbContext.EventPredictions
                .AsNoTracking()
                .Where(p => p.EventId == eventId)
                .OrderByDescending(p => p.ExpectedPoints)
                .ToListAsync();

            _logger.LogInformation(
                "Loaded {PredictionCount} predictions for event {EventId} from read model",
                predictions.Count, eventId);

            var model = new PredictionsViewModel
            {
                Event = eventEntity,
                Predictions = predictions
            };

            return View("Index", model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading predictions for event {EventId}", eventId);
            return RedirectToAction("Index");
        }
    }
}
