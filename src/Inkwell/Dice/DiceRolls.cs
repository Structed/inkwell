using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Dice;

/// <summary>
/// Rolls a <see cref="DiceNotation"/> reproducibly.
/// </summary>
/// <remarks>
/// <para>
/// Drawn through <see cref="DiceRoller"/> on a <see cref="Pcg32"/> stream rather than from
/// <c>Random</c>, for the same reason every other roll in the engine is: the algorithm is fixed
/// and externally specified, so a seed produces the same dice on every machine, every browser and
/// every future runtime. A shared roll that cannot be replayed is a rumour.
/// </para>
/// <para>
/// The stream is derived with a fixed path, so one seed means one roll. Field paths earn their keep
/// where a re-roll has to leave its neighbours undisturbed; a die has no neighbours.
/// </para>
/// </remarks>
public static class DiceRolls
{
    private const string StreamPath = "dice";

    /// <summary>A seed for a roll nobody has made yet.</summary>
    public static uint CreateSeed() => SeedCodec.CreateRandom();

    /// <summary>Rolls a fresh seed and reports both.</summary>
    public static RollOutcome Roll(DiceNotation notation) => Roll(notation, CreateSeed());

    /// <summary>Rolls <paramref name="notation"/> as <paramref name="seed"/> dictates.</summary>
    public static RollOutcome Roll(DiceNotation notation, uint seed)
    {
        ArgumentNullException.ThrowIfNull(notation);

        DiceRoller dice = new(SeedDerivation.CreateStream(seed, StreamPath));
        int[] faces = new int[notation.Count];

        for (int index = 0; index < faces.Length; index++)
        {
            faces[index] = dice.Roll(notation.Sides);
        }

        bool[] kept = Kept(faces, notation);
        RolledDie[] rolled = new RolledDie[faces.Length];
        int total = notation.Modifier;

        for (int index = 0; index < faces.Length; index++)
        {
            rolled[index] = new RolledDie(faces[index], kept[index]);

            if (kept[index])
            {
                total += faces[index];
            }
        }

        return new RollOutcome
        {
            Notation = notation,
            Dice = rolled,
            Total = total,
            Seed = seed
        };
    }

    /// <summary>
    /// Works out which dice a keep rule keeps.
    /// </summary>
    /// <remarks>
    /// Ties are settled by position, so the earlier die survives. Any rule at all would do, but it
    /// has to be stated: replaying a seed elsewhere must drop the same die, or two players looking
    /// at one roll would read different totals off it.
    /// </remarks>
    private static bool[] Kept(int[] faces, DiceNotation notation)
    {
        bool[] kept = new bool[faces.Length];

        if (notation.Keep is KeepRule.All)
        {
            Array.Fill(kept, true);
            return kept;
        }

        IEnumerable<int> ordered = notation.Keep is KeepRule.Highest
            ? Enumerable.Range(0, faces.Length).OrderByDescending(index => faces[index]).ThenBy(index => index)
            : Enumerable.Range(0, faces.Length).OrderBy(index => faces[index]).ThenBy(index => index);

        foreach (int index in ordered.Take(notation.KeepCount))
        {
            kept[index] = true;
        }

        return kept;
    }
}
