using System.Text.Json.Serialization;

namespace Structed.Inkwell.Data;

/// <summary>
/// Where a data file came from and under what licence.
/// </summary>
/// <remarks>
/// <para>
/// Carried in the data rather than hardcoded in the UI so the attribution shown to users cannot
/// drift away from the content it describes. Licences that a generator's tables are likely to come
/// under — Creative Commons, or a publisher's own third-party licence — generally require their
/// notice to be displayed in the product itself, not merely in a source repository, and that is
/// only enforceable if the notice travels with the file.
/// </para>
/// <para>
/// Non-nullable properties throughout the data models coerce null in the getter. The
/// System.Text.Json source generator discards property initialisers, so any property absent from a
/// JSON file arrives as null regardless of the <c>= ""</c> or <c>= []</c> written here.
/// </para>
/// </remarks>
public sealed record DataProvenance
{
    public string Describes { get => field ?? ""; init; } = "";

    /// <summary>Set on files that are not part of the published game, to mark them as unofficial.</summary>
    public string? Status { get; init; }

    /// <summary>Why this file exists in the form it does.</summary>
    public string? Rationale { get; init; }

    /// <summary>The upstream work this file's content comes from, if any.</summary>
    public string? Work { get; init; }

    public string? Version { get; init; }

    public string? Url { get; init; }

    public string? Licence { get; init; }

    public string? LicenceUrl { get; init; }

    /// <summary>The notice the licence requires to be displayed.</summary>
    public string? Attribution { get; init; }

    /// <summary>
    /// Further notices the licence requires, beyond <see cref="Attribution"/>.
    /// </summary>
    /// <remarks>
    /// Some third-party licences ask for more than one statement — typically one in the product and
    /// a differently worded one wherever it is listed or sold. They are kept as a list rather than
    /// as named fields because which notices are required is the licence's business, and a file
    /// under a licence that wants only one simply omits this.
    /// </remarks>
    public IReadOnlyList<string> Notices { get => field ?? []; init; } = [];

    public IReadOnlyList<string> SourceFiles { get => field ?? []; init; } = [];

    /// <summary>
    /// <c>true</c> when the file's content comes from a named upstream work rather than being
    /// original to this project.
    /// </summary>
    [JsonIgnore]
    public bool IsDerived => Work is not null;
}
