using System.Text;
using Structed.Inkwell.Data;

namespace Structed.Inkwell.Generation;

/// <summary>
/// Builds a name out of parts without making the seam obvious.
/// </summary>
/// <remarks>
/// Table-driven names are assembled from syllables or from a sign template, and both leave marks: a
/// straight join produces "Stumppond" and "Moonnest", and a filled template leaves the gaps where
/// an unused slot was. Neither problem is about what the name describes, so neither belongs in a
/// particular game's generator.
/// </remarks>
public static class NameAssembler
{
    private const string Vowels = "aeiou";

    /// <summary>
    /// Joins a name's start and end, smoothing the seam.
    /// </summary>
    public static string Join(string start, string end)
    {
        if (string.IsNullOrEmpty(start))
        {
            return Capitalise(end ?? "");
        }

        if (string.IsNullOrEmpty(end))
        {
            return Capitalise(start);
        }

        string head = start;
        char seam = char.ToLowerInvariant(head[^1]);
        char next = char.ToLowerInvariant(end[0]);

        if (seam == next)
        {
            // Oaks + stand -> Oakstand, Moon + nest -> Moonest.
            head = head[..^1];
        }
        else if (seam == 'e' && Vowels.Contains(next))
        {
            // Rose + ashe -> Rosashe, rather than the clumsier Roseashe.
            head = head[..^1];
        }

        return Capitalise(CollapseRuns(head + end));
    }

    /// <summary>
    /// Fills a name template and tidies the seams.
    /// </summary>
    /// <remarks>
    /// A slot a language does not use — English signs never mention <c>{article}</c> — resolves to
    /// an empty string and would otherwise leave a doubled space or a leading one behind.
    /// </remarks>
    public static string FromTemplate(string template, params ReadOnlySpan<(string Key, string? Value)> values) =>
        TextTemplate.Format(template, values)
            .Replace("  ", " ", StringComparison.Ordinal)
            .Trim();

    /// <summary>Upper-cases the first letter and leaves the rest alone.</summary>
    public static string Capitalise(string value) =>
        string.IsNullOrEmpty(value) ? value ?? "" : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>Reduces any run of three or more identical letters to two.</summary>
    private static string CollapseRuns(string value)
    {
        StringBuilder builder = new(value.Length);
        int run = 0;

        foreach (char c in value)
        {
            if (builder.Length > 0 && char.ToLowerInvariant(builder[^1]) == char.ToLowerInvariant(c))
            {
                run++;
                if (run >= 2)
                {
                    continue;
                }
            }
            else
            {
                run = 0;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
