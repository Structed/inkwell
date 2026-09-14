namespace Structed.Inkwell.Data;

/// <summary>
/// Articles by grammatical gender, keyed <c>m</c>, <c>f</c> or <c>n</c>.
/// </summary>
/// <remarks>
/// <para>
/// Empty in English, which has no gendered articles. In German only two forms are needed, because
/// of a convenient accident: in the dative singular after a definite article the adjective ending
/// is <c>-en</c> for every gender, so a sign reads "Zum krummen Käfer", "Zur krummen Rose" and
/// "Zum krummen Blatt". Only the article changes, so the adjective columns can ship already
/// declined and no declension code is needed anywhere.
/// </para>
/// <para>
/// The two cases are named for what they are rather than for what a particular game puts them on,
/// because the accident that makes this work is a fact about the language, not about the tables.
/// </para>
/// </remarks>
public sealed record ArticleTables
{
    /// <summary>"Ein" / "Eine", used to open a descriptive sentence.</summary>
    public IReadOnlyDictionary<string, string> IndefiniteNominative
    {
        get => field ?? new Dictionary<string, string>();
        init;
    } = new Dictionary<string, string>();

    /// <summary>"Zum" / "Zur", used on signs and dedications.</summary>
    public IReadOnlyDictionary<string, string> DativeDefinite
    {
        get => field ?? new Dictionary<string, string>();
        init;
    } = new Dictionary<string, string>();

    /// <summary>
    /// Looks up an article, returning an empty string when the language has none or the gender is
    /// unknown, so an English pattern that never mentions <c>{article}</c> costs nothing.
    /// </summary>
    public static string For(IReadOnlyDictionary<string, string> table, string? gender) =>
        gender is not null && table.TryGetValue(gender, out string? article) ? article : "";
}
