using System.Text.RegularExpressions;
using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Generation;

/// <summary>
/// Evaluates the compact dice expressions table data uses.
/// </summary>
/// <remarks>
/// Covers the forms a price or availability column tends to be written in: <c>d6</c>, <c>2d6</c>,
/// <c>d6 x 10</c>, and any of those carrying a trailing currency suffix such as <c>d6p</c> or
/// <c>4d6 coins</c>. Parsing them means the numbers come from the tables rather than being
/// duplicated in code.
/// </remarks>
public static partial class DiceExpression
{
    /// <summary>
    /// Matches a dice expression with an optional multiplier and an optional currency suffix.
    /// </summary>
    /// <remarks>
    /// The suffix is matched as letters rather than as a literal <c>p</c>, so the parser does not
    /// have one game's currency baked into it. It stays anchored and stays after the numbers, which
    /// is what keeps prose such as "not dice" or "a dozen" from being read as a roll.
    /// </remarks>
    [GeneratedRegex(@"^\s*(\d*)\s*d\s*(\d+)\s*(?:x\s*(\d+))?\s*[\p{L}]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern { get; }

    /// <summary>
    /// Rolls an expression such as <c>d6 x 10p</c>.
    /// </summary>
    /// <returns>The rolled total, or <c>null</c> when the expression is not recognised.</returns>
    public static int? TryRoll(DiceRoller dice, string? expression)
    {
        ArgumentNullException.ThrowIfNull(dice);

        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        Match match = Pattern.Match(expression);
        if (!match.Success)
        {
            return null;
        }

        int count = match.Groups[1].Value is { Length: > 0 } c ? int.Parse(c) : 1;
        int sides = int.Parse(match.Groups[2].Value);
        int multiplier = match.Groups[3].Value is { Length: > 0 } m ? int.Parse(m) : 1;

        if (count < 1 || sides < 1)
        {
            return null;
        }

        return dice.RollSum(count, sides) * multiplier;
    }

    /// <summary>Rolls an expression, falling back to <paramref name="fallback"/> if unparsable.</summary>
    public static int Roll(DiceRoller dice, string? expression, int fallback = 0) =>
        TryRoll(dice, expression) ?? fallback;
}
