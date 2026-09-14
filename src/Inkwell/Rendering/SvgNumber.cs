using System.Globalization;

namespace Structed.Inkwell.Rendering;

/// <summary>
/// Formats numbers the way SVG wants them.
/// </summary>
/// <remarks>
/// Invariantly, because a comma decimal separator under a European culture would corrupt every
/// path, and rounded, because full double precision would multiply the size of a map for no
/// visible gain.
/// </remarks>
public static class SvgNumber
{
    /// <summary>Formats a coordinate, a length or a dimension.</summary>
    public static string Format(double value) =>
        Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
}
