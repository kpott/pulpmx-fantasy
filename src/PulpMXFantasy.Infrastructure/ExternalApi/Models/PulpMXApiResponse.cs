namespace PulpMXFantasy.Infrastructure.ExternalApi.Models;

/// <summary>
/// Base API response wrapper from PulpMX Fantasy API.
/// </summary>
/// <typeparam name="T">Type of data returned in response</typeparam>
/// <remarks>
/// All PulpMX API responses follow this structure:
/// {
///   "success": true/false,
///   "data": { ... actual data ... },
///   "message": "optional error message"
/// }
///
/// This wrapper allows consistent error handling across all API calls.
/// </remarks>
public class PulpMXApiResponse<T>
{
    /// <summary>
    /// Whether the API call succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Actual data returned by the API (null if error)
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Error message if Success = false
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Alternative API response wrapper (used by next event endpoint).
/// </summary>
/// <typeparam name="T">Type of data returned in response</typeparam>
/// <remarks>
/// Some endpoints use this structure:
/// {
///   "OK": true/false,
///   "data": { ... actual data ... }
/// }
/// </remarks>
public class PulpMXApiOkResponse<T>
{
    /// <summary>
    /// Whether the API call succeeded
    /// </summary>
    public bool OK { get; set; }

    /// <summary>
    /// Actual data returned by the API
    /// </summary>
    public T? Data { get; set; }
}

/// <summary>
/// Represents an event from the PulpMX API.
/// </summary>
public class ApiEvent
{
    /// <summary>
    /// Unique slug identifier for the event (e.g., "anaheim-1-2025")
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// Display name of the event
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Venue name
    /// </summary>
    public required string Venue { get; set; }

    /// <summary>
    /// Location (city, state)
    /// </summary>
    public required string Location { get; set; }

    /// <summary>
    /// Event date in ISO 8601 format
    /// </summary>
    public required DateTimeOffset EventDate { get; set; }

    /// <summary>
    /// Round number within the series
    /// </summary>
    public int RoundNumber { get; set; }

    /// <summary>
    /// Series type (e.g., "Supercross", "Motocross")
    /// </summary>
    public required string SeriesType { get; set; }

    /// <summary>
    /// Event format (e.g., "Standard", "TripleCrown", "Motocross")
    /// </summary>
    public required string EventFormat { get; set; }

    /// <summary>
    /// Division (e.g., "East", "West", "Combined")
    /// </summary>
    public required string Division { get; set; }

    /// <summary>
    /// Whether the event has been completed
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// Fantasy pick lockout time from PulpMX API.
    /// After this time, fantasy picks are locked.
    /// </summary>
    public DateTimeOffset? LockoutTime { get; set; }

    /// <summary>
    /// List of riders participating in this event
    /// </summary>
    public List<ApiEventRider> Riders { get; set; } = new();
}

/// <summary>
/// Represents a rider's participation in a specific event from the API.
/// </summary>
public class ApiEventRider
{
    /// <summary>
    /// PulpMX identifier for the rider (e.g., "chase-sexton")
    /// </summary>
    public required string PulpMxId { get; set; }

    /// <summary>
    /// Rider's full name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Racing number
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// URL to rider's photo
    /// </summary>
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Bike class ("250" or "450")
    /// </summary>
    public required string BikeClass { get; set; }

    /// <summary>
    /// Handicap value for this event (-5 to +10 typically)
    /// </summary>
    public int Handicap { get; set; }

    /// <summary>
    /// Whether the rider is an All-Star (no doubling)
    /// </summary>
    public bool IsAllStar { get; set; }

    /// <summary>
    /// Whether the rider is currently injured
    /// </summary>
    public bool IsInjured { get; set; }

    /// <summary>
    /// Percentage of players picking this rider (0-100)
    /// </summary>
    public decimal? PickTrend { get; set; }

    /// <summary>
    /// Combined qualifying position
    /// </summary>
    public int? CombinedQualyPosition { get; set; }

    /// <summary>
    /// Best qualifying lap time in seconds
    /// </summary>
    public decimal? BestQualyLapSeconds { get; set; }

    /// <summary>
    /// Gap to fastest qualifier in seconds
    /// </summary>
    public decimal? QualyGapToLeader { get; set; }

    /// <summary>
    /// Actual finish position (null until race completes)
    /// </summary>
    public int? FinishPosition { get; set; }

    /// <summary>
    /// Finish position after handicap adjustment
    /// </summary>
    public int? HandicapAdjustedPosition { get; set; }

    /// <summary>
    /// Calculated fantasy points (null until race completes)
    /// </summary>
    public int? FantasyPoints { get; set; }

    /// <summary>
    /// Whether rider is ineligible for picking (e.g., picked last week)
    /// </summary>
    public bool Ineligible { get; set; }

    /// <summary>
    /// Reason for ineligibility (e.g., "PICKED LAST WEEK")
    /// </summary>
    public string? IneligibleReason { get; set; }
}

/// <summary>
/// Response from /v2/events/next/riders endpoint.
/// </summary>
/// <remarks>
/// ACTUAL API STRUCTURE:
/// {
///   "OK": true,
///   "data": {
///     "riders250": [...],
///     "riders450": [...],
///     "nextEventInfo": { id, type, format, title, lockout, seriesRound },
///     "deadlineExpired": false,
///     "previousTeam": {...},
///     "hasExpertPicks": bool,
///     "expertPicks": {...}
///   }
/// }
/// </remarks>
public class NextEventRidersResponse
{
    /// <summary>
    /// 250 class riders
    /// </summary>
    public List<ApiEventRiderDetailed> Riders250 { get; set; } = new();

    /// <summary>
    /// 450 class riders
    /// </summary>
    public List<ApiEventRiderDetailed> Riders450 { get; set; } = new();

    /// <summary>
    /// Basic event information
    /// </summary>
    public required NextEventInfo NextEventInfo { get; set; }

    /// <summary>
    /// Whether the deadline for picks has expired
    /// </summary>
    public bool DeadlineExpired { get; set; }
}

/// <summary>
/// Basic event information from next event endpoint.
/// </summary>
public class NextEventInfo
{
    /// <summary>
    /// Event slug (e.g., "anaheim-sx-26")
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Event type (e.g., "sx", "mx", "smx")
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Event format (e.g., "standard", "triple_crown")
    /// </summary>
    public required string Format { get; set; }

    /// <summary>
    /// Event title (e.g., "Anaheim")
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Lockout timestamp (Unix epoch seconds)
    /// </summary>
    public long Lockout { get; set; }

    /// <summary>
    /// Round number in series
    /// </summary>
    public int SeriesRound { get; set; }
}

/// <summary>
/// Response from /v2/events/{slug}/riders-with-sessions endpoint.
/// </summary>
/// <remarks>
/// ACTUAL API STRUCTURE:
/// {
///   "data": {
///     "eventData": { ... },
///     "eventId": "lasvegas-smx-24",
///     "eventType": "smx",
///     "format": "normal",
///     "riders250": [ ... ],
///     "riders450": [ ... ]
///   }
/// }
/// </remarks>
public class EventWithResultsResponse
{
    /// <summary>
    /// Event data with metadata (statistics, dates, etc.)
    /// </summary>
    public required ApiEventData EventData { get; set; }

    /// <summary>
    /// Event slug identifier (redundant with EventData.Id)
    /// </summary>
    public required string EventId { get; set; }

    /// <summary>
    /// Event type (e.g., "sx", "mx", "smx")
    /// </summary>
    public required string EventType { get; set; }

    /// <summary>
    /// Event format (e.g., "normal", "triple_crown")
    /// </summary>
    public required string Format { get; set; }

    /// <summary>
    /// 250 class riders
    /// </summary>
    public List<ApiEventRiderDetailed> Riders250 { get; set; } = new();

    /// <summary>
    /// 450 class riders
    /// </summary>
    public List<ApiEventRiderDetailed> Riders450 { get; set; } = new();
}

/// <summary>
/// Detailed event data from API (eventData object).
/// </summary>
public class ApiEventData
{
    /// <summary>
    /// Event slug ID (e.g., "lasvegas-smx-24")
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Series ID number
    /// </summary>
    public int SeriesId { get; set; }

    /// <summary>
    /// Event type (e.g., "sx", "mx", "smx")
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Event title (e.g., "Las Vegas SMX")
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Display label (e.g., "3. Las Vegas SMX")
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// Pretty formatted date string
    /// </summary>
    public required string PrettyDate { get; set; }

    /// <summary>
    /// Lockout timestamp (Unix epoch seconds as string)
    /// </summary>
    public required string Lockout { get; set; }

    /// <summary>
    /// Round number in series
    /// </summary>
    public int SeriesRound { get; set; }

    /// <summary>
    /// Event format (e.g., "normal", "triple_crown")
    /// </summary>
    public required string Format { get; set; }

    /// <summary>
    /// Region (for 250 East/West split)
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Event status (e.g., "complete", "upcoming")
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Whether event is currently in progress
    /// </summary>
    public bool InProgress { get; set; }

    /// <summary>
    /// Whether results are official
    /// </summary>
    public bool ResultsOfficial { get; set; }

    /// <summary>
    /// Series title (e.g., "2024 AMA SuperMotocross")
    /// </summary>
    public string? SeriesTitle { get; set; }

    /// <summary>
    /// Series label (e.g., "SMX 24")
    /// </summary>
    public string? SeriesLabel { get; set; }

    /// <summary>
    /// LCQ cutoff position for 250 class
    /// </summary>
    public int? LcqCutoff250 { get; set; }

    /// <summary>
    /// LCQ cutoff position for 450 class
    /// </summary>
    public int? LcqCutoff450 { get; set; }
}

/// <summary>
/// Detailed rider data from /riders-with-sessions endpoint.
/// </summary>
/// <remarks>
/// This includes additional fields compared to ApiEventRider:
/// - Session results (qualifying, motos)
/// - Pick trends
/// - Series points
/// </remarks>
public class ApiEventRiderDetailed
{
    /// <summary>
    /// Rider UUID
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Rider ID (same as Id, redundant)
    /// </summary>
    public required string RiderId { get; set; }

    /// <summary>
    /// Rider slug (e.g., "pierce-brown")
    /// </summary>
    public required string Slug { get; set; }

    /// <summary>
    /// Full name (e.g., "Pierce Brown")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// First name
    /// </summary>
    public required string NameFirst { get; set; }

    /// <summary>
    /// Last name
    /// </summary>
    public required string NameLast { get; set; }

    /// <summary>
    /// Racing number as string (e.g., "39")
    /// </summary>
    public required string Number { get; set; }

    /// <summary>
    /// Racing number as integer
    /// </summary>
    public int NumberInt { get; set; }

    /// <summary>
    /// Bike class ("250" or "450")
    /// </summary>
    public required string BikeClass { get; set; }

    /// <summary>
    /// Bike class (alternate field name, same as BikeClass)
    /// </summary>
    public required string Class { get; set; }

    /// <summary>
    /// Handicap value (-5 to +10 typically)
    /// </summary>
    public int Handicap { get; set; }

    /// <summary>
    /// Handicap-adjusted finish position
    /// </summary>
    public int? HandicapPosition { get; set; }

    /// <summary>
    /// Whether rider is All-Star (no doubling)
    /// </summary>
    public bool AllStar { get; set; }

    /// <summary>
    /// Whether rider is injured
    /// </summary>
    public bool Injured { get; set; }

    /// <summary>
    /// Pick trend as percentage (can be string or number from API)
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? PickTrend { get; set; }

    /// <summary>
    /// Combined qualifying position
    /// </summary>
    public int? CombinedQualyPosition { get; set; }

    /// <summary>
    /// Best qualifying lap time (e.g., "1:38.799")
    /// </summary>
    public string? BestQualyLap { get; set; }

    /// <summary>
    /// Best qualifying lap in seconds (can be number or string from API)
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(FlexibleDecimalConverter))]
    public decimal? BestQualyLapSeconds { get; set; }

    /// <summary>
    /// Session where best lap was set (e.g., "qualifying2")
    /// </summary>
    public string? BestQualyLapSession { get; set; }

    /// <summary>
    /// Actual finish position (null until race completes)
    /// </summary>
    public int? FinishPosition { get; set; }

    /// <summary>
    /// Position (alternate field, same as FinishPosition)
    /// </summary>
    public int? Position { get; set; }

    /// <summary>
    /// Fantasy points earned
    /// </summary>
    public int? FantasyPoints { get; set; }

    /// <summary>
    /// Total fantasy points (same as FantasyPoints for normal format)
    /// </summary>
    public int? TotalFantasyPoints { get; set; }

    /// <summary>
    /// Official AMA points earned
    /// </summary>
    public int? OfficialPoints { get; set; }

    /// <summary>
    /// Series points before this event
    /// </summary>
    public int? SeriesPoints { get; set; }

    /// <summary>
    /// Series points after this event
    /// </summary>
    public int? NewSeriesPoints { get; set; }

    /// <summary>
    /// Image URL
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Whether rider has an image
    /// </summary>
    public bool HasImage { get; set; }

    /// <summary>
    /// Session results (moto1, moto2, qualifying1, qualifying2, etc.)
    /// </summary>
    public Dictionary<string, ApiSessionData>? Sessions { get; set; }

    /// <summary>
    /// Whether rider is ineligible for picking (e.g., picked last week)
    /// </summary>
    public bool Ineligible { get; set; }

    /// <summary>
    /// Reason for ineligibility (e.g., "PICKED LAST WEEK")
    /// </summary>
    public string? IneligibleReason { get; set; }
}

/// <summary>
/// Session data for a rider (qualifying, moto, etc.).
/// </summary>
public class ApiSessionData
{
    /// <summary>
    /// Best lap time (e.g., "1:38.053")
    /// </summary>
    public string? BestLap { get; set; }

    /// <summary>
    /// Position in this session
    /// </summary>
    public int? Position { get; set; }

    /// <summary>
    /// Fantasy points for this session (for Triple Crown/Motocross)
    /// </summary>
    public int? FantasyPoints { get; set; }

    /// <summary>
    /// Best lap in seconds
    /// </summary>
    public decimal? BestLapSeconds { get; set; }

    /// <summary>
    /// Official points earned in this session
    /// </summary>
    public int? OfficialPoints { get; set; }

    /// <summary>
    /// Handicap-adjusted position for this session
    /// </summary>
    public int? HandicapPosition { get; set; }
}

/// <summary>
/// Represents a qualifying or practice session.
/// </summary>
public class ApiSession
{
    /// <summary>
    /// Session name (e.g., "Qualifying 1", "Free Practice")
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Session type
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Session results
    /// </summary>
    public List<ApiSessionResult> Results { get; set; } = new();
}

/// <summary>
/// Represents a rider's result in a qualifying/practice session.
/// </summary>
public class ApiSessionResult
{
    /// <summary>
    /// Rider's PulpMX ID
    /// </summary>
    public required string PulpMxId { get; set; }

    /// <summary>
    /// Position in the session
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Best lap time in seconds
    /// </summary>
    public decimal? BestLap { get; set; }

    /// <summary>
    /// Gap to leader in seconds
    /// </summary>
    public decimal? GapToLeader { get; set; }
}
