using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Generation;

/// <summary>
/// Resolves a single field: either its pinned value, or a roll from that field's own stream.
/// </summary>
/// <remarks>
/// Every value in a generated result is addressed by a path such as <c>shop/2/keeper/quirk</c>.
/// Because each path gets an independent stream derived from the root seed, re-rolling one field
/// cannot disturb any other, and pinning a field survives a re-roll of everything around it.
/// </remarks>
public sealed class RollContext(RollPlan plan)
{
    private readonly RollPlan _plan =
        plan ?? throw new ArgumentNullException(nameof(plan));

    private readonly Dictionary<string, string> _pinValues = new(StringComparer.Ordinal);

    public RollPlan Plan => _plan;

    /// <summary>
    /// What each field would be pinned to if the user locked it.
    /// </summary>
    /// <remarks>
    /// Recorded as the roll happens, because only the generator knows which row of which table a
    /// value came from. Locking then stores that position rather than the words, so the lock
    /// survives a change of language.
    /// </remarks>
    public IReadOnlyDictionary<string, string> PinValues => _pinValues;

    /// <summary>Notes what would be stored if <paramref name="path"/> were locked.</summary>
    public void Record(string path, string pinValue) => _pinValues[path] = pinValue;

    /// <summary>Creates the dice for one field.</summary>
    public DiceRoller Dice(string path) =>
        new(SeedDerivation.CreateStream(_plan.Seed, StreamKey(path)));

    /// <summary>
    /// The stream key for a path, incorporating how many times it has been individually re-rolled
    /// so that each re-roll draws from a different stream.
    /// </summary>
    private string StreamKey(string path) =>
        _plan.Rerolls.TryGetValue(path, out int count) && count > 0
            ? $"{path}#{count}"
            : path;

    /// <summary>Returns the pinned value for a path, if there is one.</summary>
    public bool TryGetPin(string path, out string value)
    {
        if (_plan.Pins.TryGetValue(path, out string? pinned) && pinned is not null)
        {
            value = pinned;
            return true;
        }

        value = "";
        return false;
    }

    /// <summary>Picks one entry from a table, honouring a pin on that path.</summary>
    public string Text(string path, IReadOnlyList<string> table)
    {
        if (TryGetPin(path, out string pinned))
        {
            Record(path, pinned);
            return PinReference.Resolve(pinned, table);
        }

        if (table.Count == 0)
        {
            Record(path, "");
            return "";
        }

        int index = Dice(path).NextIndex(table.Count);
        Record(path, PinReference.ForIndex(index));

        return table[index];
    }

    /// <summary>
    /// Picks several distinct entries from a table. Pins on this path store the whole selection as
    /// a newline-separated block so that a multi-valued field such as a city's two industries can
    /// be locked as a unit.
    /// </summary>
    public IReadOnlyList<string> TextMany(string path, IReadOnlyList<string> table, int count)
    {
        if (TryGetPin(path, out string pinned))
        {
            Record(path, pinned);
            return PinReference.ResolveMany(pinned, table);
        }

        if (table.Count == 0)
        {
            Record(path, "");
            return [];
        }

        IReadOnlyList<int> indices = Dice(path).PickDistinctIndices(table.Count, count);
        Record(path, PinReference.ForIndices(indices));

        return [.. indices.Select(index => table[index])];
    }
}
