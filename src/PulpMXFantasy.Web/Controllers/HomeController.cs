using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.ReadModel;
using PulpMXFantasy.Web.Models;

namespace PulpMXFantasy.Web.Controllers;

/// <summary>
/// Home controller - Dashboard and main navigation.
/// </summary>
/// <remarks>
/// CQRS Pattern: Uses only ReadDbContext for read operations.
/// NO access to write models (enforced at assembly level).
/// </remarks>
public class HomeController : Controller
{
    private readonly ReadDbContext _readDbContext;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        ReadDbContext readDbContext,
        ILogger<HomeController> logger)
    {
        _readDbContext = readDbContext;
        _logger = logger;
    }

    /// <summary>
    /// Dashboard - Shows next upcoming event and quick stats from read models.
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

            // Count total events from read model
            var eventCount = await _readDbContext.Events.CountAsync();

            // Sum total riders from events
            var totalRiders = await _readDbContext.Events
                .SumAsync(e => e.RiderCount);

            var model = new DashboardViewModel
            {
                NextEvent = nextEvent,
                TotalRiders = totalRiders,
                TotalEvents = eventCount
            };

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard");
            return View(new DashboardViewModel());
        }
    }

    /// <summary>
    /// Privacy policy page.
    /// </summary>
    public IActionResult Privacy()
    {
        return View();
    }

    /// <summary>
    /// Error page.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
