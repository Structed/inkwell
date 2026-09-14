namespace Structed.Inkwell.Rendering;

/// <summary>
/// How a rendered map should present itself to the page around it.
/// </summary>
/// <remarks>
/// Everything here is the caller's to decide: the engine knows how to draw a map but not what the
/// thing on it is called, nor what the host page's stylesheet hooks onto.
/// </remarks>
public sealed record MapRenderOptions
{
    /// <summary>The accessible label. Falls back to the map's subject when not given.</summary>
    /// <remarks>
    /// Already phrased and already translated. The engine escapes it but does not compose it,
    /// because the wording belongs to whichever app is showing the map.
    /// </remarks>
    public string? AriaLabel { get; init; }

    /// <summary>The class written onto the root element, for the page's stylesheet to target.</summary>
    public string CssClass { get; init; } = "place-map";

    /// <summary>
    /// Writes the map's size onto the root element as well as into the <c>viewBox</c>.
    /// </summary>
    /// <remarks>
    /// Off by default because a page usually wants a map that fills its frame, which is what a bare
    /// <c>viewBox</c> gives. It has to be on for any copy that leaves the page: an SVG with no
    /// intrinsic dimensions has no natural size to be drawn at, so an <c>&lt;img&gt;</c> or an image
    /// viewer falls back to the default object size of 300×150 and the map arrives as a thumbnail.
    /// A page that sets the width in CSS is unaffected either way, because CSS beats a presentation
    /// attribute.
    /// </remarks>
    public bool IntrinsicSize { get; init; }
}
