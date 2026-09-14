namespace Structed.Inkwell.Generation;

/// <summary>
/// How a locked value is written down.
/// </summary>
/// <remarks>
/// <para>
/// A lock has to mean "this row of this table" rather than "these words". Storing the words works
/// perfectly well in one language and falls apart in two: lock a settlement's industry in German,
/// switch to English, and the sheet would keep one German line in the middle of an English page.
/// </para>
/// <para>
/// So a value drawn from a table is pinned as <c>#12</c>, its position, which resolves through
/// whichever language's table is loaded. Anything the user typed themselves is stored verbatim,
/// because their words are theirs and no table can reproduce them.
/// </para>
/// </remarks>
public static class PinReference
{
    private const char Marker = '#';

    /// <summary>Writes a pin that refers to a table row by position.</summary>
    public static string ForIndex(int index) => $"{Marker}{index}";

    /// <summary>Writes a pin for several rows, as a locked pair of values from one table would need.</summary>
    public static string ForIndices(IEnumerable<int> indices) =>
        string.Join('\n', indices.Select(ForIndex));

    /// <summary>Whether this pin points at a table row rather than carrying literal text.</summary>
    public static bool IsReference(string pin) =>
        TryGetIndex(pin, out _);

    /// <summary>Reads the position out of a pin, if it holds one.</summary>
    public static bool TryGetIndex(string? pin, out int index)
    {
        index = -1;

        return pin is { Length: > 1 } &&
               pin[0] == Marker &&
               int.TryParse(pin.AsSpan(1), out index) &&
               index >= 0;
    }

    /// <summary>
    /// Turns a pin back into text.
    /// </summary>
    /// <remarks>
    /// A reference that no longer fits the table — because the table shrank, or an old export
    /// pinned a row that has since gone — falls back to the pin as written rather than throwing,
    /// so a stale file still opens.
    /// </remarks>
    public static string Resolve(string pin, IReadOnlyList<string> table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return TryGetIndex(pin, out int index) && index < table.Count ? table[index] : pin;
    }

    /// <summary>Resolves a multi-line pin, one line per locked value.</summary>
    public static IReadOnlyList<string> ResolveMany(string pin, IReadOnlyList<string> table) =>
    [
        .. pin
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Resolve(line, table))
    ];
}
