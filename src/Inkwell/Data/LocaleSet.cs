using System.Collections;

namespace Structed.Inkwell.Data;

/// <summary>
/// The languages a project ships, in the order a picker should list them.
/// </summary>
/// <remarks>
/// Exists so the engine does not have one project's languages compiled into it. Enumerating the set
/// yields the locales, so it drops straight into a <c>foreach</c> or a LINQ query where a list was
/// used before.
/// </remarks>
public sealed class LocaleSet : IReadOnlyList<Locale>
{
    private readonly Locale[] _locales;

    /// <summary>Declares the shipped languages. Exactly one must be canonical.</summary>
    /// <exception cref="ArgumentException">
    /// The set is empty, repeats a code, or does not name exactly one canonical language. Each is a
    /// wiring mistake that would otherwise surface as a silently untranslated or unloadable app.
    /// </exception>
    public LocaleSet(params IReadOnlyList<Locale> locales)
    {
        ArgumentNullException.ThrowIfNull(locales);

        if (locales.Count == 0)
        {
            throw new ArgumentException("A project ships at least one language.", nameof(locales));
        }

        if (locales.Select(l => l.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != locales.Count)
        {
            throw new ArgumentException("Two languages share a code.", nameof(locales));
        }

        Locale[] canonical = [.. locales.Where(l => l.IsCanonical)];
        if (canonical.Length != 1)
        {
            throw new ArgumentException(
                $"Exactly one language is canonical, but {canonical.Length} are. The canonical language " +
                "is the one the data files are written in, so it is what every overlay is merged onto.",
                nameof(locales));
        }

        _locales = [.. locales];
        Canonical = canonical[0];
    }

    /// <summary>The language the data files are written in.</summary>
    public Locale Canonical { get; }

    public int Count => _locales.Length;

    public Locale this[int index] => _locales[index];

    /// <summary>
    /// Resolves a locale from a code, tolerating the region-tagged forms a browser reports such as
    /// <c>de-AT</c> or <c>en-GB</c>. Falls back to <see cref="Canonical"/> rather than throwing,
    /// because the input is usually a browser setting or a URL parameter.
    /// </summary>
    public Locale FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Canonical;
        }

        ReadOnlySpan<char> trimmed = code.AsSpan().Trim();
        int separator = trimmed.IndexOfAny('-', '_');
        if (separator > 0)
        {
            trimmed = trimmed[..separator];
        }

        foreach (Locale locale in _locales)
        {
            if (trimmed.Equals(locale.Code, StringComparison.OrdinalIgnoreCase))
            {
                return locale;
            }
        }

        return Canonical;
    }

    public IEnumerator<Locale> GetEnumerator() => ((IEnumerable<Locale>)_locales).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _locales.GetEnumerator();
}
