using System.Net;
using System.Text;
using Structed.Inkwell.Mapping;
using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Rendering;

/// <summary>
/// Renders a map to SVG in an ink-on-paper style.
/// </summary>
/// <remarks>
/// Geometry is wobbled by <see cref="RoughPen"/> rather than by an SVG filter, because filters are
/// raster operations: they would blur under zoom and displace shared edges apart from one another.
/// A turbulence filter is still used, but only for the paper grain behind everything, where those
/// drawbacks do not matter.
/// </remarks>
public static class SvgMapRenderer
{
    /// <summary>Renders the map.</summary>
    /// <param name="map">The generated map.</param>
    /// <param name="seed">Seeds the pen's wobble, so a redraw is reproducible.</param>
    /// <param name="options">
    /// How the caller wants the map presented. Optional so that geometry tests need not decide.
    /// </param>
    public static string Render(PlaceMap map, uint seed, MapRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        MapRenderOptions presentation = options ?? new MapRenderOptions();
        RoughPen pen = new(SeedDerivation.CreateStream(seed, "map/render"));

        string ariaLabel = Escape(
            presentation.AriaLabel is { Length: > 0 } label ? label : map.Subject);

        string size = presentation.IntrinsicSize
            ? $""" width="{RoughPen.N(map.Width)}" height="{RoughPen.N(map.Height)}" """
            : " ";

        StringBuilder svg = new();

        svg.Append(
            $"""
             <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {RoughPen.N(map.Width)} {RoughPen.N(map.Height)}"{size}
             role="img" aria-label="{ariaLabel}" class="{Escape(presentation.CssClass)}">
             """);

        AppendDefs(svg, seed);

        svg.Append($"""<rect width="100%" height="100%" fill="var(--paper, #f6f1e4)" />""");
        svg.Append($"""<rect width="100%" height="100%" fill="#000" opacity="0.05" filter="url(#paper-grain)" />""");

        AppendHost(svg, pen, map);
        AppendWater(svg, pen, map);
        AppendRoads(svg, pen, map);
        AppendScatter(svg, pen, map);
        AppendBuildings(svg, pen, map);
        AppendKeys(svg, map);

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static void AppendDefs(StringBuilder svg, uint seed)
    {
        // The turbulence seed is derived from the map seed so the grain is reproducible too.
        int grainSeed = (int)(seed % 1000);

        svg.Append(
            $"""
             <defs>
               <filter id="paper-grain" x="0" y="0" width="100%" height="100%">
                 <feTurbulence type="fractalNoise" baseFrequency="0.8" numOctaves="3" seed="{grainSeed}" />
                 <feColorMatrix type="saturate" values="0" />
               </filter>
               <clipPath id="host-clip">
                 <use href="#host-outline" />
               </clipPath>
             </defs>
             """);
    }

    /// <summary>The silhouette of the host object, which frames the whole map.</summary>
    private static void AppendHost(StringBuilder svg, RoughPen pen, PlaceMap map)
    {
        string outline = pen.ClosedPath(map.Boundary.Points, 0.7);

        svg.Append("""<g class="map-host">""");
        svg.Append($"""<path id="host-outline" d="{outline}" fill="#fffdf7" stroke="none" />""");

        // Drawn twice, the second pass lighter, so the edge reads as ink rather than as a vector.
        svg.Append($"""<path d="{outline}" fill="none" stroke="#1a1713" stroke-width="2.6" stroke-linejoin="round" />""");
        svg.Append($"""<path d="{pen.ClosedPath(map.Boundary.Points, 0.35)}" fill="none" stroke="#1a1713" stroke-width="1.1" opacity="0.55" />""");
        svg.Append("</g>");
    }

    private static void AppendWater(StringBuilder svg, RoughPen pen, PlaceMap map)
    {
        if (map.Water is not { } water)
        {
            return;
        }

        svg.Append("""<g class="map-water" clip-path="url(#host-clip)">""");
        svg.Append($"""<path d="{pen.ClosedPath(water.Points, 0.5)}" fill="#dfe8ea" stroke="none" />""");

        svg.Append("""<g stroke="#5b7f8a" stroke-width="0.7" fill="none" opacity="0.75">""");
        foreach (string stroke in pen.Hatch(water.Points, 7, -20))
        {
            svg.Append($"""<path d="{stroke}" />""");
        }

        svg.Append("</g>");
        svg.Append($"""<path d="{pen.ClosedPath(water.Points, 0.5)}" fill="none" stroke="#3f5f6a" stroke-width="1.4" />""");
        svg.Append("</g>");
    }

    /// <summary>
    /// Roads, drawn as a dark casing with a lighter fill on top.
    /// </summary>
    /// <remarks>
    /// Stroking the same path twice at different widths is what turns a bare line into an outlined
    /// road without having to compute its two edges.
    /// </remarks>
    private static void AppendRoads(StringBuilder svg, RoughPen pen, PlaceMap map)
    {
        if (map.Roads.Count == 0)
        {
            return;
        }

        List<(string Path, double Width)> paths = [];
        foreach (RoadSegment road in map.Roads)
        {
            paths.Add((pen.Line(road.From, road.To, 0.5), road.Width));
        }

        svg.Append("""<g class="map-roads" stroke-linecap="round" fill="none">""");

        foreach ((string path, double width) in paths)
        {
            svg.Append($"""<path d="{path}" stroke="#1a1713" stroke-width="{RoughPen.N(width + 2.2)}" />""");
        }

        foreach ((string path, double width) in paths)
        {
            svg.Append($"""<path d="{path}" stroke="#ece4d2" stroke-width="{RoughPen.N(width)}" />""");
        }

        svg.Append("</g>");
    }

    private static void AppendScatter(StringBuilder svg, RoughPen pen, PlaceMap map)
    {
        if (map.Scatter.Count == 0)
        {
            return;
        }

        svg.Append("""<g class="map-scatter" stroke="#4c5b3c" fill="none" stroke-width="0.9">""");

        foreach (MapScatter item in map.Scatter)
        {
            double x = item.Position.X;
            double y = item.Position.Y;
            double r = item.Size;

            switch (item.Kind)
            {
                case ScatterKind.Tree:
                    svg.Append($"""<circle cx="{RoughPen.N(x)}" cy="{RoughPen.N(y)}" r="{RoughPen.N(r)}" fill="#e4ecdb" />""");
                    svg.Append($"""<path d="{pen.Line(new MapPoint(x, y + r), new MapPoint(x, y + r + 3), 0.6)}" />""");
                    break;

                case ScatterKind.Shrub:
                    svg.Append($"""<circle cx="{RoughPen.N(x - (r / 3))}" cy="{RoughPen.N(y)}" r="{RoughPen.N(r * 0.7)}" fill="#e4ecdb" />""");
                    svg.Append($"""<circle cx="{RoughPen.N(x + (r / 3))}" cy="{RoughPen.N(y + 1)}" r="{RoughPen.N(r * 0.6)}" fill="#e4ecdb" />""");
                    break;

                default:
                    svg.Append($"""<path d="{pen.ClosedPath(
                        [
                            new MapPoint(x - r, y + (r / 2)),
                            new MapPoint(x - (r / 2), y - (r / 2)),
                            new MapPoint(x + (r / 2), y - (r / 3)),
                            new MapPoint(x + r, y + (r / 2))
                        ], 0.5)}" fill="#e6e1d4" stroke="#6b6355" />""");
                    break;
            }
        }

        svg.Append("</g>");
    }

    private static void AppendBuildings(StringBuilder svg, RoughPen pen, PlaceMap map)
    {
        if (map.Buildings.Count == 0)
        {
            return;
        }

        svg.Append("""<g class="map-buildings">""");

        foreach (MapBuilding building in map.Buildings)
        {
            IReadOnlyList<MapPoint> corners = building.Corners();
            string outline = pen.ClosedPath(corners);

            string fill = building.IsKeyed ? "#e8d9c4" : "#fbf6ea";

            svg.Append($"""<path d="{outline}" fill="{fill}" stroke="#1a1713" stroke-width="{(building.IsKeyed ? "1.8" : "1.3")}" stroke-linejoin="round" />""");

            // A ridge line down the long axis reads as a roof and gives each footprint some depth.
            MapPoint ridgeStart = corners[0].Lerp(corners[3], 0.5);
            MapPoint ridgeEnd = corners[1].Lerp(corners[2], 0.5);

            svg.Append($"""<path d="{pen.Line(ridgeStart, ridgeEnd, 0.7)}" fill="none" stroke="#1a1713" stroke-width="0.7" opacity="0.5" />""");
        }

        svg.Append("</g>");
    }

    /// <summary>The numbered discs that tie buildings to the legend.</summary>
    private static void AppendKeys(StringBuilder svg, PlaceMap map)
    {
        IReadOnlyList<MapBuilding> keyed = [.. map.Buildings.Where(b => b.IsKeyed)];
        if (keyed.Count == 0)
        {
            return;
        }

        svg.Append("""<g class="map-keys" font-family="Helvetica, Arial, sans-serif" font-size="9" font-weight="700" text-anchor="middle">""");

        foreach (MapBuilding building in keyed)
        {
            double x = building.Centre.X;
            double y = building.Centre.Y;

            svg.Append($"""<circle cx="{RoughPen.N(x)}" cy="{RoughPen.N(y)}" r="7" fill="#1a1713" />""");
            svg.Append($"""<text x="{RoughPen.N(x)}" y="{RoughPen.N(y + 3.2)}" fill="#f6f1e4">{building.Key}</text>""");

            if (building.Label is { Length: > 0 } label)
            {
                svg.Append($"""<title>{Escape(label)}</title>""");
            }
        }

        svg.Append("</g>");
    }

    private static string Escape(string value) => WebUtility.HtmlEncode(value);
}
