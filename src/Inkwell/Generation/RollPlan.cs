namespace Structed.Inkwell.Generation;

/// <summary>
/// The seed, locks and re-rolls that a generated result is a pure function of.
/// </summary>
/// <remarks>
/// <para>
/// Generation is a pure function of a plan, which is what makes a seed URL meaningful: the same
/// plan always rebuilds the same result.
/// </para>
/// <para>
/// <see cref="Pins"/> and <see cref="Rerolls"/> together implement "lock the bits you like". A
/// pinned path keeps its value through a re-roll, while bumping a path's re-roll counter changes
/// only that one value and leaves its neighbours untouched.
/// </para>
/// <para>
/// A generator inherits from this and adds its own settings, so that the <c>with</c> helpers it
/// writes return its own type. The pin and re-roll bookkeeping is identical for every generator,
/// so it lives here; what a settlement, a dungeon or a city is configured by does not.
/// </para>
/// </remarks>
public abstract record RollPlan
{
    /// <summary>The root seed all values are derived from.</summary>
    public uint Seed { get; init; }

    /// <summary>Values held fixed across a re-roll, keyed by field path.</summary>
    public IReadOnlyDictionary<string, string> Pins { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// How many times each path has been individually re-rolled. The count is folded into the
    /// field's stream, so bumping it yields a different value without disturbing anything else.
    /// </summary>
    public IReadOnlyDictionary<string, int> Rerolls { get; init; } =
        new Dictionary<string, int>();

    public bool IsPinned(string path) => Pins.ContainsKey(path);

    /// <summary>The pins with <paramref name="path"/> pinned to <paramref name="value"/>.</summary>
    protected IReadOnlyDictionary<string, string> PinsWith(string path, string value) =>
        new Dictionary<string, string>(Pins, StringComparer.Ordinal) { [path] = value };

    /// <summary>The pins without <paramref name="path"/>.</summary>
    protected IReadOnlyDictionary<string, string> PinsWithout(string path)
    {
        Dictionary<string, string> pins = new(Pins, StringComparer.Ordinal);
        pins.Remove(path);
        return pins;
    }

    /// <summary>The pins with every path under <paramref name="pathPrefix"/> removed.</summary>
    protected IReadOnlyDictionary<string, string> PinsWithoutPrefix(string pathPrefix)
    {
        Dictionary<string, string> pins = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pin in Pins)
        {
            if (!pin.Key.StartsWith(pathPrefix, StringComparison.Ordinal))
            {
                pins[pin.Key] = pin.Value;
            }
        }

        return pins;
    }

    /// <summary>The re-roll counts with <paramref name="path"/>'s bumped by one.</summary>
    /// <remarks>
    /// A pinned field would ignore the new roll, so a caller re-rolling a path should clear its pin
    /// with <see cref="PinsWithout"/> in the same step.
    /// </remarks>
    protected IReadOnlyDictionary<string, int> RerollsWith(string path)
    {
        Dictionary<string, int> rerolls = new(Rerolls, StringComparer.Ordinal)
        {
            [path] = Rerolls.GetValueOrDefault(path) + 1
        };

        return rerolls;
    }
}
