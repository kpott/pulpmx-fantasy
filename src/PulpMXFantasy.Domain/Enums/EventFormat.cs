namespace PulpMXFantasy.Domain.Enums;

/// <summary>
/// Represents the format/structure of a racing event.
/// </summary>
/// <remarks>
/// Different event formats have different scoring rules and race structures:
/// - Standard: Single main event (most common)
/// - TripleCrown: 3 shorter races, scored by overall position across all 3
/// - Motocross: 2 motos, fantasy points awarded PER MOTO (summed)
/// - SuperMotocross: Playoff format with unique rules
///
/// CRITICAL: Scoring logic must branch based on format!
/// - Standard: 1 finish position → fantasy points
/// - TripleCrown: 3 finish positions → overall position → fantasy points
/// - Motocross: 2 separate moto finishes → 2 sets of fantasy points (summed)
/// </remarks>
public enum EventFormat
{
    /// <summary>
    /// Standard single main event format (most Supercross events)
    /// </summary>
    Standard,

    /// <summary>
    /// Triple Crown format - 3 shorter main events, scored by overall position
    /// Example: 2nd, 5th, 1st = 8 total points = 3rd overall
    /// FFL: +15 if rider leads ANY of the 3 first laps
    /// </summary>
    TripleCrown,

    /// <summary>
    /// Motocross format - 2 motos, fantasy points awarded PER MOTO
    /// Example: Moto 1 = 25 pts, Moto 2 = 22 pts → Total = 47 pts
    /// Requires separate moto_results table
    /// </summary>
    Motocross,

    /// <summary>
    /// SuperMotocross playoff format - special rules
    /// </summary>
    SuperMotocross
}
