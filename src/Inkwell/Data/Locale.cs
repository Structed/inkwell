using System.Globalization;

namespace Structed.Inkwell.Data;

/// <summary>
/// A language a set of data files is published in.
/// </summary>
/// <remarks>
/// <para>
/// One locale in a project is canonical: the data files are written in it, and every key in them —
/// ids, dice expressions, category names and any name another file joins on — stays in it in every
/// language. Other locales supply a translation overlay that replaces display text only.
/// </para>
/// <para>
/// Which languages a project ships, and which of them is canonical, is the project's business
/// rather than the engine's. A locale is therefore constructed rather than chosen from a fixed
/// list, and the set of them is a <see cref="LocaleSet"/>.
/// </para>
/// </remarks>
public sealed record Locale
{
    /// <summary>Declares a language.</summary>
    /// <param name="code">The ISO 639-1 code, e.g. <c>de</c>.</param>
    /// <param name="nativeName">The language's name in itself, e.g. "Deutsch".</param>
    /// <param name="englishName">The language's English name, which is what diagnostics read best in.</param>
    /// <param name="isCanonical">
    /// <c>true</c> for the language the data files themselves are written in, which therefore needs
    /// no overlay. Exactly one locale in a <see cref="LocaleSet"/> may set it.
    /// </param>
    public Locale(string code, string nativeName, string englishName, bool isCanonical = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        NativeName = nativeName ?? code;
        EnglishName = englishName ?? code;
        IsCanonical = isCanonical;
    }

    /// <summary>The ISO 639-1 code, e.g. <c>de</c>.</summary>
    public string Code { get; }

    /// <summary>The language's name in itself, e.g. "Deutsch", which is what a picker should show.</summary>
    public string NativeName { get; }

    public string EnglishName { get; }

    /// <summary>
    /// <c>true</c> for the language the data files are written in, which therefore needs no overlay.
    /// </summary>
    public bool IsCanonical { get; }

    /// <summary>
    /// English, declared canonical, which is the usual arrangement and saves most projects
    /// restating it. A project that writes its data files in another language declares its own.
    /// </summary>
    public static Locale English { get; } = new("en", "English", "English", isCanonical: true);

    /// <summary>The data root a locale's overlay files live under, e.g. <c>i18n/de</c>.</summary>
    public string OverlayRoot => $"i18n/{Code}";

    /// <summary>
    /// How to format numbers for a reader of this language.
    /// </summary>
    /// <remarks>
    /// Resolved once and used explicitly, rather than by setting the ambient culture: Blazor
    /// WebAssembly will not allow the app culture to change unless the entire ICU dataset is
    /// bundled, and a megabyte of globalization data is a poor trade for a thousands separator.
    /// Falls back to the invariant culture, because a purse printed with the wrong separator is a
    /// far smaller problem than a page that will not render.
    /// </remarks>
    public CultureInfo FormatCulture => field ??= Resolve(Code);

    private static CultureInfo Resolve(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public override string ToString() => Code;
}
