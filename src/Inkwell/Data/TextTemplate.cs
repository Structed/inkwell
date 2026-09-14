using System.Text;

namespace Structed.Inkwell.Data;

/// <summary>
/// Fills <c>{placeholder}</c> slots in a localised format string.
/// </summary>
/// <remarks>
/// Named placeholders rather than <see cref="string.Format(string, object?[])"/>'s numbered ones,
/// because a translator reorders a sentence far more often than not: German pushes the verb to the
/// end, and "A village of 150-300 mice" becomes "Ein Dorf mit 150-300 Mäusen". A name survives that
/// reordering; an index silently swaps two values.
/// </remarks>
public static class TextTemplate
{
    /// <summary>
    /// Replaces every <c>{key}</c> in <paramref name="template"/> with its value.
    /// </summary>
    /// <remarks>
    /// A placeholder with no matching value is left in place rather than blanked, so a missing
    /// substitution shows up as a visible <c>{host}</c> in the UI instead of a hole in a sentence.
    /// </remarks>
    public static string Format(string template, params ReadOnlySpan<(string Key, string? Value)> values)
    {
        if (string.IsNullOrEmpty(template) || values.Length == 0)
        {
            return template ?? "";
        }

        StringBuilder builder = new(template);

        foreach (int i in LongestKeyFirst(values))
        {
            (string key, string? value) = values[i];
            builder.Replace($"{{{key}}}", value ?? "");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Orders the substitutions so that a longer key is filled before a shorter one it starts with.
    /// </summary>
    /// <remarks>
    /// <c>{noun}</c> filled before <c>{noun2}</c> eats the longer slot's prefix and leaves a stray
    /// "2" in the middle of a name. Deciding the order here means a caller cannot get it wrong by
    /// listing its values in the order the sentence reads.
    /// </remarks>
    private static int[] LongestKeyFirst(ReadOnlySpan<(string Key, string? Value)> values)
    {
        int[] order = new int[values.Length];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        for (int i = 1; i < order.Length; i++)
        {
            for (int j = i; j > 0 && values[order[j]].Key.Length > values[order[j - 1]].Key.Length; j--)
            {
                (order[j], order[j - 1]) = (order[j - 1], order[j]);
            }
        }

        return order;
    }
}
