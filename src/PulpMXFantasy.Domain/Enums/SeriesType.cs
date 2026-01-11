namespace PulpMXFantasy.Domain.Enums;

/// <summary>
/// Represents the racing series/championship type.
/// </summary>
/// <remarks>
/// Different series have different rules, formats, and seasons:
/// - Supercross: Indoor stadium racing, January-May
/// - Motocross: Outdoor racing, May-August
/// - SuperMotocross: Playoff format combining SX and MX, September
/// </remarks>
public enum SeriesType
{
    /// <summary>
    /// Supercross - Indoor stadium racing series (17 rounds)
    /// </summary>
    Supercross,

    /// <summary>
    /// Motocross - Outdoor racing series (12 rounds)
    /// </summary>
    Motocross,

    /// <summary>
    /// SuperMotocross - Playoff championship combining top riders from SX and MX
    /// </summary>
    SuperMotocross
}
