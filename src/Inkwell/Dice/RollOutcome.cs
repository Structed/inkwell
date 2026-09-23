using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Dice;

/// <summary>One die, as it landed.</summary>
/// <param name="Face">What it came up.</param>
/// <param name="IsKept">
/// <c>false</c> for a die that was rolled and then dropped by a keep rule. Dropped dice are still
/// reported, because watching the 1 you had to throw away is most of the point of <c>4d6kh3</c>.
/// </param>
public readonly record struct RolledDie(int Face, bool IsKept);

/// <summary>
/// What one roll produced, and the seed that would produce it again.
/// </summary>
/// <remarks>
/// The seed travels with the result everywhere it goes. It is what lets a roll be pasted into a chat
/// window and rebuilt exactly by somebody whose browser could not reach the table directly — the
/// same bargain the rest of the engine makes with its links.
/// </remarks>
public sealed record RollOutcome
{
    public required DiceNotation Notation { get; init; }

    public required IReadOnlyList<RolledDie> Dice { get; init; }

    /// <summary>The kept dice plus the modifier.</summary>
    public required int Total { get; init; }

    public required uint Seed { get; init; }

    /// <summary>The seed as it is written down: short, and safe to paste.</summary>
    public string SeedCode => SeedCodec.Encode(Seed);

    /// <summary>Just the faces, in the order they were rolled.</summary>
    public IEnumerable<int> Faces => Dice.Select(die => die.Face);

    /// <summary>How many dice came up exactly <paramref name="face"/>, kept or not.</summary>
    /// <remarks>
    /// Dropped dice count here on purpose. A pool that reads its faces rather than summing them has
    /// no keep rule to apply, and a rule that counted only kept dice would quietly disagree with the
    /// dice lying on the table.
    /// </remarks>
    public int CountOf(int face) => Dice.Count(die => die.Face == face);
}
