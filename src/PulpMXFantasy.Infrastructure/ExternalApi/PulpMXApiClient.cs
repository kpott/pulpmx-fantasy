using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PulpMXFantasy.Infrastructure.ExternalApi.Models;

namespace PulpMXFantasy.Infrastructure.ExternalApi;

/// <summary>
/// Implementation of PulpMX Fantasy API client with retry logic and error handling.
/// </summary>
/// <remarks>
/// RETRY STRATEGY (configured via IHttpClientFactory + Polly):
/// ============================================================
/// 1. **Standard Retry** - Transient HTTP errors (5xx, timeouts)
///    - 3 retries with exponential backoff: 2s, 4s, 8s
///    - Applies to GET requests only (idempotent)
///
/// 2. **Circuit Breaker** - Too many consecutive failures
///    - Opens after 5 consecutive failures
///    - Stays open for 30 seconds
///    - Prevents cascading failures
///
/// 3. **Timeout** - Per-request timeout
///    - 30 seconds per request
///    - Applies to all requests
///
/// These are configured in DI registration, not in this class.
/// See Infrastructure/DependencyInjection.cs for configuration.
///
/// ERROR HANDLING STRATEGY:
/// ========================
/// 1. Network errors (no connection) → HttpRequestException
/// 2. API errors (success = false) → PulpMXApiException
/// 3. Timeouts → TimeoutException
/// 4. Invalid JSON → JsonException
/// 5. Unexpected errors → Exception
///
/// Callers should catch these exceptions and implement fallback:
/// - Use cached data if API unavailable
/// - Display user-friendly error messages
/// - Log errors for monitoring
///
/// JSON SERIALIZATION:
/// ===================
/// Uses System.Text.Json with:
/// - Case-insensitive property matching
/// - CamelCase naming convention
/// - Null value handling (ignore nulls)
/// </remarks>
public class PulpMXApiClient : IPulpMXApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PulpMXApiClient> _logger;
    private readonly PulpMXApiOptions _options;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _cacheDirectory;

    /// <summary>
    /// Constructor with dependency injection.
    /// </summary>
    /// <param name="httpClient">HttpClient configured with retry policies</param>
    /// <param name="options">API configuration options</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <remarks>
    /// HttpClient is injected from IHttpClientFactory which:
    /// - Manages HttpClient lifetime (prevents socket exhaustion)
    /// - Applies Polly retry policies configured in DI
    /// - Adds default headers (API key, User-Agent)
    /// </remarks>
    public PulpMXApiClient(
        HttpClient httpClient,
        IOptions<PulpMXApiOptions> options,
        ILogger<PulpMXApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        // Configure HttpClient base address and default headers
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", _options.ApiToken);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PulpMXFantasy/1.0");

        // Initialize cache directory
        _cacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), "ApiCache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    /// <inheritdoc />
    public async Task<NextEventRidersResponse?> GetNextEventAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching next event from PulpMX API");

        try
        {
            var response = await _httpClient.GetAsync("/v2/events/next/riders", cancellationToken);

            // Handle 404 specially - means no upcoming events
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("No upcoming events found (404)");
                return null;
            }

            response.EnsureSuccessStatusCode();

            // Note: This endpoint uses { "OK": true, "data": {...} } format
            var apiResponse = await response.Content.ReadFromJsonAsync<PulpMXApiOkResponse<NextEventRidersResponse>>(
                _jsonOptions,
                cancellationToken);

            if (apiResponse?.OK == false)
            {
                throw new PulpMXApiException(
                    $"API returned error for next event",
                    (int)response.StatusCode);
            }

            if (apiResponse?.Data == null)
            {
                _logger.LogInformation("No upcoming events found (null data)");
                return null;
            }

            _logger.LogInformation(
                "Successfully fetched next event: {EventName} ({EventId}) on {Lockout} with {Rider250Count} 250 riders and {Rider450Count} 450 riders",
                apiResponse.Data.NextEventInfo.Title,
                apiResponse.Data.NextEventInfo.Id,
                DateTimeOffset.FromUnixTimeSeconds(apiResponse.Data.NextEventInfo.Lockout),
                apiResponse.Data.Riders250.Count,
                apiResponse.Data.Riders450.Count);

            return apiResponse.Data;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error calling PulpMX API");
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize API response");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<EventWithResultsResponse> GetEventWithResultsAsync(
        string eventSlug,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheKey = $"event-{eventSlug}";
        var cachedResponse = await GetCachedResponseAsync<EventWithResultsResponse>(cacheKey);
        if (cachedResponse != null)
        {
            _logger.LogInformation(
                "Using cached event data for {EventSlug}: {EventName}",
                eventSlug,
                cachedResponse.EventData.Title);
            return cachedResponse;
        }

        _logger.LogInformation("Fetching event with results from API: {EventSlug}", eventSlug);

        try
        {
            var response = await _httpClient.GetAsync(
                $"/v2/events/{eventSlug}/riders-with-sessions",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            // Note: This endpoint does NOT use the standard PulpMXApiResponse wrapper
            // It returns data directly in format: { "data": { "eventData": {...}, "riders250": [...], "riders450": [...] } }
            var rawResponse = await response.Content.ReadFromJsonAsync<Dictionary<string, EventWithResultsResponse>>(
                _jsonOptions,
                cancellationToken);

            var eventData = rawResponse?["data"];

            if (eventData == null)
            {
                throw new PulpMXApiException(
                    $"Event {eventSlug} not found or invalid response format",
                    (int)response.StatusCode);
            }

            _logger.LogInformation(
                "Successfully fetched event: {EventName} with {Rider250Count} 250 riders and {Rider450Count} 450 riders",
                eventData.EventData.Title,
                eventData.Riders250.Count,
                eventData.Riders450.Count);

            // Save to cache for future use
            await SaveToCacheAsync(cacheKey, eventData);

            return eventData;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching event {EventSlug}", eventSlug);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize event response for {EventSlug}", eventSlug);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ApiEvent>> GetSeriesEventsAsync(
        int year,
        string seriesType,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching series events: {Year} {SeriesType}", year, seriesType);

        try
        {
            var response = await _httpClient.GetAsync(
                $"/v2/series/{year}/{seriesType}",
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<PulpMXApiResponse<List<ApiEvent>>>(
                _jsonOptions,
                cancellationToken);

            if (apiResponse?.Success == false)
            {
                throw new PulpMXApiException(
                    $"API returned error for series {year} {seriesType}: {apiResponse.Message}",
                    (int)response.StatusCode,
                    apiResponse.Message);
            }

            var events = apiResponse?.Data ?? new List<ApiEvent>();

            _logger.LogInformation(
                "Successfully fetched {EventCount} events for {Year} {SeriesType}",
                events.Count,
                year,
                seriesType);

            return events;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching series {Year} {SeriesType}", year, seriesType);
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize series response for {Year} {SeriesType}", year, seriesType);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ApiEvent>> GetEventsAsync(
        string? status = null,
        string? orderBy = null,
        string? orderDir = null,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Fetching events with filters - status: {Status}, orderBy: {OrderBy}, orderDir: {OrderDir}, limit: {Limit}",
            status ?? "all",
            orderBy ?? "none",
            orderDir ?? "none",
            limit?.ToString() ?? "none");

        try
        {
            // Build query string
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(status))
                queryParams.Add($"status={Uri.EscapeDataString(status)}");
            if (!string.IsNullOrEmpty(orderBy))
                queryParams.Add($"orderBy={Uri.EscapeDataString(orderBy)}");
            if (!string.IsNullOrEmpty(orderDir))
                queryParams.Add($"orderDir={Uri.EscapeDataString(orderDir)}");
            if (limit.HasValue)
                queryParams.Add($"limit={limit.Value}");

            var queryString = queryParams.Count > 0 ? "?" + string.Join("&", queryParams) : "";
            var url = $"/v2/events{queryString}";

            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<PulpMXApiResponse<List<ApiEvent>>>(
                _jsonOptions,
                cancellationToken);

            if (apiResponse?.Success == false)
            {
                throw new PulpMXApiException(
                    $"API returned error for events query: {apiResponse.Message}",
                    (int)response.StatusCode,
                    apiResponse.Message);
            }

            var events = apiResponse?.Data ?? new List<ApiEvent>();

            _logger.LogInformation(
                "Successfully fetched {EventCount} events",
                events.Count);

            return events;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error fetching events");
            throw;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize events response");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to fetch next event as a health check
            // Use a short timeout for health checks
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await _httpClient.GetAsync("/v2/events/next/riders", cts.Token);

            // Any 2xx or 404 (no events) is considered healthy
            return response.IsSuccessStatusCode ||
                   response.StatusCode == System.Net.HttpStatusCode.NotFound;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PulpMX API health check failed");
            return false;
        }
    }

    /// <summary>
    /// Gets cached API response from disk if available.
    /// </summary>
    private async Task<T?> GetCachedResponseAsync<T>(string cacheKey) where T : class
    {
        try
        {
            var cacheFile = Path.Combine(_cacheDirectory, $"{cacheKey}.json");

            if (!File.Exists(cacheFile))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(cacheFile);
            var response = JsonSerializer.Deserialize<T>(json, _jsonOptions);

            if (response != null)
            {
                _logger.LogDebug("Using cached response for {CacheKey}", cacheKey);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reading cache for {CacheKey}", cacheKey);
            return null;
        }
    }

    /// <summary>
    /// Saves API response to disk cache.
    /// </summary>
    private async Task SaveToCacheAsync<T>(string cacheKey, T data) where T : class
    {
        try
        {
            var cacheFile = Path.Combine(_cacheDirectory, $"{cacheKey}.json");
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            await File.WriteAllTextAsync(cacheFile, json);

            _logger.LogDebug("Saved response to cache: {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error saving cache for {CacheKey}", cacheKey);
            // Don't throw - caching is optional
        }
    }
}

/// <summary>
/// Configuration options for PulpMX API client.
/// </summary>
/// <remarks>
/// These options are populated from appsettings.json and environment variables:
/// {
///   "PulpMXApi": {
///     "BaseUrl": "https://api.pulpmxfantasy.com",
///     "ApiToken": "from-environment-variable",
///     "TimeoutSeconds": 30,
///     "RetryCount": 3
///   }
/// }
///
/// ApiToken should NEVER be in appsettings.json directly!
/// - Development: Use User Secrets
/// - Production: Use Azure Key Vault or environment variable
/// </remarks>
public class PulpMXApiOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "PulpMXApi";

    /// <summary>
    /// Base URL of the PulpMX Fantasy API
    /// </summary>
    /// <remarks>
    /// Default: https://api.pulpmxfantasy.com
    /// </remarks>
    public string BaseUrl { get; set; } = "https://api.pulpmxfantasy.com";

    /// <summary>
    /// API authentication token
    /// </summary>
    /// <remarks>
    /// CRITICAL: Must come from environment variable or Key Vault!
    /// NEVER commit this to source control.
    /// </remarks>
    public required string ApiToken { get; set; }

    /// <summary>
    /// Timeout in seconds for each API request
    /// </summary>
    /// <remarks>
    /// Default: 30 seconds
    /// Applied per request, not including retries
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of retry attempts for transient failures
    /// </summary>
    /// <remarks>
    /// Default: 3 retries
    /// Applied only to transient HTTP errors (5xx, timeouts)
    /// </remarks>
    public int RetryCount { get; set; } = 3;
}
