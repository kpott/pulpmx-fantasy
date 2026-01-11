namespace PulpMXFantasy.Domain.Enums;

/// <summary>
/// Represents the 250 class division split in Supercross.
/// </summary>
/// <remarks>
/// CRITICAL for 250 class rider filtering!
///
/// In Supercross, the 250 class is split into:
/// - East division (races at certain rounds)
/// - West division (races at different rounds)
/// - Showdown (special events where both East and West compete)
/// - Combined (riders who can race any division, or used for 450 class)
///
/// This affects:
/// 1. Which riders are available for fantasy picks at each event
/// 2. ML model training (East vs West have different competition levels)
/// 3. Database queries MUST filter:
///    - East events: WHERE division IN ('East', 'Combined')
///    - West events: WHERE division IN ('West', 'Combined')
///    - Showdown events: WHERE division IN ('East', 'West', 'Showdown', 'Combined')
///
/// Example: If event is "Showdown", show riders from all 250 divisions
/// </remarks>
public enum Division
{
    /// <summary>
    /// East division - races at eastern US venues
    /// </summary>
    East,

    /// <summary>
    /// West division - races at western US venues
    /// </summary>
    West,

    /// <summary>
    /// Combined/Both divisions - can race any event
    /// Used for 450 class (no split) and special cases
    /// </summary>
    Combined,

    /// <summary>
    /// Showdown - special event where both East and West compete together
    /// These are marquee 250 events where all divisions race
    /// </summary>
    Showdown
}
