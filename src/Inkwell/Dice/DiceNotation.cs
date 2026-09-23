using System.Text.RegularExpressions;

namespace Structed.Inkwell.Dice;

/// <summary>Which of the dice rolled actually count towards the total.</summary>
public enum KeepRule
{
    /// <summary>Every die counts.</summary>
    All,

    /// <summary>Only the highest few count; the rest are rolled and shown, but dropped.</summary>
    Highest,

    /// <summary>Only the lowest few count.</summary>
    Lowest
}

/// <summary>
/// A dice roll as a player writes it: <c>d20</c>, <c>2d6+1</c>, <c>4d6kh3</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <em>not</em> <see cref="Structed.Inkwell.Generation.DiceExpression"/>, which reads
/// what a table says — <c>d6 x 10p</c> — and answers with a total. This one is typed by a person
/// into a box at the table, has to keep every individual face so the party can see the dice, and
/// has to be able to refuse politely rather than throw: somebody will type <c>2d</c> mid-thought
/// and the page must not fall over. The two are different jobs sitting in one package, so they do
/// not share a name.
/// </para>
/// <para>
/// Everything here is bounded. A roll on a shared channel is a thing other people's browsers have to
/// render, so <c>99999d1000</c> is not a clever edge case to handle gracefully — it is a refusal.
/// </para>
/// </remarks>
public sealed partial record DiceNotation
{
    /// <summary>The most dice one roll may ask for.</summary>
    public const int MaximumCount = 100;

    /// <summary>A one-sided die is not a die.</summary>
    public const int MinimumSides = 2;

    public const int MaximumSides = 1000;

    public const int MaximumModifier = 1000;

    /// <summary>The longest string <see cref="TryParse"/> will even look at.</summary>
    public const int MaximumLength = 32;

    private DiceNotation(int count, int sides, KeepRule keep, int keepCount, int modifier)
    {
        Count = count;
        Sides = sides;
        Keep = keep;
        KeepCount = keepCount;
        Modifier = modifier;
    }

    /// <summary>How many dice are rolled.</summary>
    public int Count { get; }

    /// <summary>How many faces each of them has.</summary>
    public int Sides { get; }

    public KeepRule Keep { get; }

    /// <summary>How many dice count, when <see cref="Keep"/> is not <see cref="KeepRule.All"/>.</summary>
    public int KeepCount { get; }

    /// <summary>Added to the total after the dice are read; may be negative.</summary>
    public int Modifier { get; }

    /// <summary>
    /// The canonical spelling, which is what travels to the other players.
    /// </summary>
    /// <remarks>
    /// Rebuilt from the parsed numbers rather than echoing what was typed, so <c>2 D 6 + 1</c>
    /// arrives in everybody else's log as <c>2d6+1</c>, and two people writing the same roll two
    /// ways do not appear to have rolled different things.
    /// </remarks>
    public string Text
    {
        get
        {
            string keep = Keep switch
            {
                KeepRule.Highest => $"kh{KeepCount}",
                KeepRule.Lowest => $"kl{KeepCount}",
                _ => ""
            };

            string modifier = Modifier switch
            {
                > 0 => $"+{Modifier}",
                < 0 => Modifier.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => ""
            };

            return $"{Count}d{Sides}{keep}{modifier}";
        }
    }

    public override string ToString() => Text;

    /// <summary>A plain pool of <paramref name="count"/> dice with <paramref name="sides"/> faces.</summary>
    /// <remarks>For presets, which build a notation from a number rather than from typing.</remarks>
    public static DiceNotation? Pool(int count, int sides) =>
        Create(count, sides, KeepRule.All, count, 0);

    /// <summary>Reads a notation, answering <c>false</c> rather than throwing on nonsense.</summary>
    public static bool TryParse(string? text, out DiceNotation notation)
    {
        notation = null!;

        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumLength)
        {
            return false;
        }

        Match match = Pattern.Match(text.Replace(" ", "", StringComparison.Ordinal));

        if (!match.Success)
        {
            return false;
        }

        if (!TryReadNumber(match.Groups[1].Value, 1, out int count) ||
            !TryReadNumber(match.Groups[2].Value, 0, out int sides) ||
            !TryReadNumber(match.Groups[4].Value, 1, out int keepCount) ||
            !TryReadNumber(match.Groups[6].Value, 0, out int modifierSize))
        {
            return false;
        }

        KeepRule keep = match.Groups[3].Value.ToUpperInvariant() switch
        {
            "H" => KeepRule.Highest,
            "L" => KeepRule.Lowest,
            _ => KeepRule.All
        };

        DiceNotation? parsed = Create(
            count,
            sides,
            keep,
            keepCount,
            match.Groups[5].Value == "-" ? -modifierSize : modifierSize);

        if (parsed is null)
        {
            return false;
        }

        notation = parsed;
        return true;
    }

    /// <summary>
    /// Matches <c>[count]d[sides][kh|kl count][+|-modifier]</c>, anchored at both ends.
    /// </summary>
    /// <remarks>
    /// Anchored so that a roll is the whole of what was typed. An unanchored pattern would quietly
    /// find <c>d6</c> inside "the d6 one" and roll something the writer did not ask for — and this
    /// text goes straight into other people's logs.
    /// </remarks>
    [GeneratedRegex(@"^(\d*)d(\d+)(?:k([hl])(\d*))?(?:([+-])(\d+))?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern { get; }

    /// <summary>Builds a notation if every part of it is within bounds, and <c>null</c> otherwise.</summary>
    private static DiceNotation? Create(int count, int sides, KeepRule keep, int keepCount, int modifier)
    {
        if (count is < 1 or > MaximumCount ||
            sides is < MinimumSides or > MaximumSides ||
            Math.Abs(modifier) > MaximumModifier ||
            keepCount < 1)
        {
            return null;
        }

        // Keeping at least as many dice as were rolled is keeping all of them; say so, so that two
        // spellings of one roll compare equal and read the same in the log.
        if (keep is not KeepRule.All && keepCount >= count)
        {
            keep = KeepRule.All;
        }

        return new DiceNotation(count, sides, keep, keep is KeepRule.All ? count : keepCount, modifier);
    }

    /// <summary>Reads one captured number, treating an absent group as <paramref name="fallback"/>.</summary>
    private static bool TryReadNumber(string captured, int fallback, out int value)
    {
        if (captured.Length == 0)
        {
            value = fallback;
            return true;
        }

        // Anything longer than this is out of bounds whatever it says, and parsing it risks
        // overflowing rather than refusing.
        if (captured.Length > 4)
        {
            value = 0;
            return false;
        }

        return int.TryParse(captured, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
