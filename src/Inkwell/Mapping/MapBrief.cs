namespace Structed.Inkwell.Mapping;

/// <summary>
/// Everything the map generator needs to know about the place it is drawing.
/// </summary>
/// <remarks>
/// The generator lays out roads and buildings inside a silhouette; it has no opinion about what the
/// silhouette is or why the place has a mill. Reducing a generated place to a brief keeps that
/// ignorance honest, and means the same layout engine serves a settlement inside an oak hollow and
/// a town in a river valley without either one leaking into it.
/// </remarks>
public sealed record MapBrief
{
    /// <summary>
    /// The layout archetype, one of <c>hollow</c>, <c>linear</c>, <c>vessel</c>, <c>boxy</c>,
    /// <c>warren</c> or <c>sprawl</c>. Anything else is drawn as a hollow.
    /// </summary>
    public string Shape { get => field ?? ""; init; } = "";

    /// <summary>
    /// How big the place is, from 1 for a farmstead to 6 for a city. Decides the road and building
    /// budgets, and how far a narrow silhouette opens up to accommodate them.
    /// </summary>
    public int Scale { get; init; } = 1;

    /// <summary>The name of the thing the map is drawn inside, used to label it.</summary>
    public string Subject { get => field ?? ""; init; } = "";

    /// <summary>
    /// Whether a band of water crosses the map. What implies water is the caller's judgement: a
    /// riverside trade, a terrain, or an explicit setting.
    /// </summary>
    public bool HasWater { get; init; }

    /// <summary>
    /// The things that earn a numbered key, in the order they should be numbered.
    /// </summary>
    /// <remarks>
    /// The order is the caller's and is preserved exactly, because a key number is visible to the
    /// player and is part of how a lock is addressed. Keys are handed to the most prominent
    /// buildings first, so the first subject listed lands on the main street.
    /// </remarks>
    public IReadOnlyList<MapKeySubject> Keys { get => field ?? []; init; } = [];
}

/// <summary>Something on the map worth a number in the legend.</summary>
/// <param name="Name">What it is called, e.g. "The Crooked Beetle".</param>
/// <param name="Detail">The legend's second line, already phrased in the reader's language.</param>
public sealed record MapKeySubject(string Name, string Detail);
