namespace Structed.Inkwell.Dice;

/// <summary>
/// The single number a preset asks for before it can build its dice.
/// </summary>
/// <param name="Id">Names the number for the wording file; never shown as-is.</param>
/// <param name="Minimum">The smallest sensible answer.</param>
/// <param name="Maximum">The largest.</param>
/// <param name="Default">What the box starts at.</param>
/// <remarks>
/// One number, not a form. A preset that needed three inputs would be a rules engine, and the
/// notation box is already there for anyone who wants to spell out something stranger.
/// </remarks>
public sealed record RollParameter(string Id, int Minimum, int Maximum, int Default)
{
    /// <summary>Drags <paramref name="value"/> back inside the bounds.</summary>
    /// <remarks>
    /// Clamped rather than rejected because this arrives from a number box, where browsers will
    /// happily hand over an empty string, a pasted <c>999</c>, or whatever a spin button did on the
    /// way past.
    /// </remarks>
    public int Clamp(int value) => Math.Clamp(value, Minimum, Maximum);
}

/// <summary>
/// What a preset makes of the dice once they have landed.
/// </summary>
/// <param name="Key">
/// Names a line in the wording file. No sentence is built here, so a preset can be read in any
/// language the site later grows without any of this having to change.
/// </param>
/// <param name="Value">The number that line talks about — a count of successes, a target, a margin.</param>
public sealed record RollReading(string Key, int Value);

/// <summary>
/// How much the odds have been bent, in steps.
/// </summary>
/// <remarks>
/// <para>
/// A signed count rather than a three-way switch, because advantages stack and cancel: two
/// advantages and a disadvantage are one advantage, and the arithmetic says so without anybody
/// writing a rule for it.
/// </para>
/// <para>
/// What a step <em>does</em> is not decided here. A pool might get another die; a single d20 might
/// be rolled twice and the kinder result kept. Both are one step, and the preset that owns the dice
/// is the only thing that knows which.
/// </para>
/// </remarks>
public static class RollEdge
{
    /// <summary>No amount of stacking bends the odds further than this.</summary>
    public const int Most = 2;

    /// <summary>Drags a step count back inside the bounds.</summary>
    public static int Clamp(int steps) => Math.Clamp(steps, -Most, Most);
}

/// <summary>
/// A named roll a player can make without spelling out the notation.
/// </summary>
/// <remarks>
/// <para>
/// Presets exist because the dice are rarely the interesting part. "Attack with three dice" and
/// "save against a difficulty of 12" are what actually gets said at the table; <c>3d6</c> and
/// <c>1d20</c> are the arithmetic underneath, and having to translate one into the other every time
/// is exactly the friction a dice tool ought to remove.
/// </para>
/// <para>
/// Nothing in this type knows any game. It holds an id, a number to ask for, a way to turn that
/// number into dice, and a way to read the dice back as a key and a value. The game lives entirely
/// in the functions handed to it, which is why a preset can be built here and the rules that gave
/// rise to it can stay in the tool that owns them.
/// </para>
/// </remarks>
public sealed record RollPreset
{
    /// <summary>Names the preset for the wording file, and identifies it in the markup.</summary>
    public required string Id { get; init; }

    /// <summary>The number to ask for, or <c>null</c> if the preset needs nothing.</summary>
    public RollParameter? Parameter { get; init; }

    /// <summary>Builds the dice from the settled parameter and the edge, in that order.</summary>
    public required Func<int, int, DiceNotation> Notation { get; init; }

    /// <summary>Reads the landed dice, or <c>null</c> to leave the total to speak for itself.</summary>
    public Func<RollOutcome, int, RollReading?>? Reading { get; init; }

    /// <summary>
    /// Anything else the dice happen to show, beyond how the roll went.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Reading"/> because these are not the answer to the roll — they are
    /// the raw material a character sheet might make something of. A roll can succeed and still owe
    /// the roller a second attack, and the two facts need saying side by side rather than one
    /// overwriting the other.
    /// </para>
    /// <para>
    /// Handed the faces rather than the whole outcome, so that anyone holding nothing but a list of
    /// numbers — a screen across the table, say, that received the dice and would rather not take
    /// the sender's word for what they add up to — can work these out again for themselves.
    /// </para>
    /// </remarks>
    public Func<IReadOnlyList<int>, int, IReadOnlyList<RollReading>>? Notes { get; init; }

    /// <summary>Whether the edge control is worth offering for this preset.</summary>
    public bool HasEdge { get; init; }

    /// <summary>The parameter as it will actually be used, clamped and defaulted.</summary>
    public int Settle(int? value) =>
        Parameter is null ? 0 : Parameter.Clamp(value ?? Parameter.Default);

    /// <summary>The dice this preset would roll, which is also what the player is shown before rolling.</summary>
    public DiceNotation Dice(int? value, int edge) =>
        Notation(Settle(value), HasEdge ? RollEdge.Clamp(edge) : 0);

    /// <summary>Rolls the preset, with a fresh seed.</summary>
    public RollOutcome Roll(int? value, int edge = 0) => Roll(value, edge, DiceRolls.CreateSeed());

    /// <summary>Rolls the preset as <paramref name="seed"/> dictates.</summary>
    public RollOutcome Roll(int? value, int edge, uint seed) =>
        DiceRolls.Roll(Dice(value, edge), seed);

    /// <summary>Reads an outcome this preset produced.</summary>
    public RollReading? Read(RollOutcome outcome, int? value)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return Reading?.Invoke(outcome, Settle(value));
    }

    /// <summary>Everything else the outcome is worth saying, which may be nothing.</summary>
    public IReadOnlyList<RollReading> Note(RollOutcome outcome, int? value)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return Note([.. outcome.Faces], value);
    }

    /// <summary>Everything else a bare list of faces is worth saying.</summary>
    public IReadOnlyList<RollReading> Note(IReadOnlyList<int> faces, int? value) =>
        Notes?.Invoke(faces, Settle(value)) ?? [];
}
