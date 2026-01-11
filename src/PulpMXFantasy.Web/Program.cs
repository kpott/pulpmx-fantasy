using Microsoft.EntityFrameworkCore;
using PulpMXFantasy.Messaging;
using PulpMXFantasy.ReadModel;
using Serilog;

// ============================================================================
// SERILOG CONFIGURATION
// ============================================================================
// Configure Serilog BEFORE building the application.
// This ensures logging captures startup errors and configuration issues.

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger(); // Temporary logger for startup

Log.Information("Starting PulpMX Fantasy application...");

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Replace default logging with Serilog
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PulpMXFantasy")
        .Enrich.WithMachineName()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// ============================================================================
// DEPENDENCY INJECTION - CQRS WEB (READ-ONLY + SEND COMMANDS)
// ============================================================================
// Web UI follows CQRS pattern:
// - READS from read_model schema ONLY (via ReadDbContext)
// - SENDS commands via MassTransit (no business logic)
// - NO ML/prediction services (those are in Worker)
// - NO ApplicationDbContext (enforced at assembly level)
//
// This assembly boundary prevents accidental access to write models or ML services.

// Add MVC controllers with views
builder.Services.AddControllersWithViews();

// ============================================================================
// READ MODEL DATABASE (CQRS - READ SIDE)
// ============================================================================
// Register ReadDbContext for read_model schema access.
// Web queries ONLY from read models - all writes happen in Worker.

builder.Services.AddReadModel(builder.Configuration);

// ============================================================================
// MASSTRANSIT (CQRS - SEND ONLY)
// ============================================================================
// Configure MassTransit for sending commands to Worker Service.
// Web UI only sends commands - no consumers registered here.

builder.Services.AddMessagingSendOnly(builder.Configuration);

// ============================================================================
// HEALTH CHECKS
// ============================================================================
// Register health checks for monitoring. Web only checks database connectivity.
// ML model and external API checks are in Worker service.

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string 'DefaultConnection' is not configured.");
}

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: connectionString,
        name: "postgresql",
        tags: new[] { "db", "database", "postgresql" });

// ============================================================================
// BUILD APPLICATION
// ============================================================================

    var app = builder.Build();

// ============================================================================
// MIDDLEWARE PIPELINE
// ============================================================================
// Order matters! Middleware executes in the order registered.

    // Serilog request logging (logs HTTP requests with timing)
    app.UseSerilogRequestLogging(options =>
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
            diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress);
        };
    });

// Development: Show detailed error pages
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Production: Show generic error page
    app.UseExceptionHandler("/Home/Error");

    // HSTS: Force HTTPS (31536000 seconds = 1 year)
    app.UseHsts();
}

// Redirect HTTP -> HTTPS
app.UseHttpsRedirection();

// Enable routing
app.UseRouting();

// Enable authorization (future: add [Authorize] attributes)
app.UseAuthorization();

// Serve static files (wwwroot: CSS, JS, images)
app.MapStaticAssets();

// Map controller routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

    // ============================================================================
    // HEALTH CHECK ENDPOINTS
    // ============================================================================
    // Map health check endpoints for monitoring and load balancer probes.

    // /health - All health checks (returns 200 OK if all healthy)
    app.MapHealthChecks("/health");

    // /health/ready - Readiness probe (checks database)
    // Use for: Kubernetes readiness probe, load balancer health check
    app.MapHealthChecks("/health/ready");

    // /health/live - Liveness probe (basic app health, no external dependencies)
    // Use for: Kubernetes liveness probe (restart if unhealthy)
    app.MapHealthChecks("/health/live", new()
    {
        Predicate = _ => false // No checks - just verifies app is running
    });

// ============================================================================
// DATABASE MIGRATION
// ============================================================================
// Automatically apply pending migrations on startup.
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // Apply ReadDbContext migrations
        var readDbContext = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
        logger.LogInformation("Applying ReadDbContext migrations...");
        await readDbContext.Database.MigrateAsync();
        logger.LogInformation("ReadDbContext migrations applied successfully");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations");
        throw; // Fail fast if migrations fail
    }
}

    // ============================================================================
    // RUN APPLICATION
    // ============================================================================

    Log.Information("Application started successfully");
    app.Run();
    Log.Information("Application stopped gracefully");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    // Flush logs and clean up
    Log.CloseAndFlush();
}
