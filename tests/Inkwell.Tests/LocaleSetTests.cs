using Structed.Inkwell.Data;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers the language set a project declares.
/// </summary>
/// <remarks>
/// The validation matters more than it looks. A set with no canonical language has nothing to merge
/// overlays onto, and a set with two has no single answer to what the data files are written in.
/// Both are wiring mistakes that would otherwise surface much later as an app that loads no text.
/// </remarks>
public class LocaleSetTests
{
    private static readonly Locale German = new("de", "Deutsch", "German");
    private static readonly Locale Portuguese = new("pt", "Português", "Portuguese");

    [Fact]
    public void CanonicalIsTheDeclaredOne()
    {
        LocaleSet set = new(Locale.English, German);

        Assert.Equal("en", set.Canonical.Code);
    }

    [Fact]
    public void AnyLanguageMayBeCanonical()
    {
        Locale canonicalGerman = new("de", "Deutsch", "German", isCanonical: true);
        LocaleSet set = new(canonicalGerman, new Locale("en", "English", "English"));

        Assert.Equal("de", set.Canonical.Code);
        Assert.False(set.FromCode("en").IsCanonical);
    }

    [Fact]
    public void EnumeratesInDeclaredOrder() =>
        Assert.Equal(["en", "de", "pt"], new LocaleSet(Locale.English, German, Portuguese).Select(l => l.Code));

    [Theory]
    [InlineData("de", "de")]
    [InlineData("DE", "de")]
    [InlineData(" de ", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de_CH", "de")]
    [InlineData("en-GB", "en")]
    public void ResolvesRegionTaggedCodes(string code, string expected) =>
        Assert.Equal(expected, new LocaleSet(Locale.English, German).FromCode(code).Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fr")]
    [InlineData("-de")]
    public void FallsBackToCanonicalRatherThanThrowing(string? code) =>
        Assert.Equal("en", new LocaleSet(Locale.English, German).FromCode(code).Code);

    [Fact]
    public void RejectsASetWithNoCanonicalLanguage()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => new LocaleSet(German, Portuguese));

        Assert.Contains("Exactly one language is canonical", ex.Message);
    }

    [Fact]
    public void RejectsASetWithTwoCanonicalLanguages()
    {
        Locale alsoCanonical = new("de", "Deutsch", "German", isCanonical: true);

        Assert.Throws<ArgumentException>(() => new LocaleSet(Locale.English, alsoCanonical));
    }

    [Fact]
    public void RejectsARepeatedCode() =>
        Assert.Throws<ArgumentException>(() => new LocaleSet(Locale.English, German, new Locale("DE", "x", "x")));

    [Fact]
    public void RejectsAnEmptySet() => Assert.Throws<ArgumentException>(() => new LocaleSet());

    [Fact]
    public void CanonicalNeedsNoOverlayButOthersDo()
    {
        Assert.True(Locale.English.IsCanonical);
        Assert.Equal("i18n/de", German.OverlayRoot);
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("not a language")]
    [InlineData("!!")]
    public void NeverThrowsResolvingAFormatCultureForAnOddCode(string code) =>
        Assert.NotNull(new Locale(code, "?", "?").FormatCulture);
}
