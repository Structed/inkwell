using System.Globalization;
using System.Xml.Linq;
using Structed.Inkwell.Mapping;
using Structed.Inkwell.Rendering;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers turning a map into SVG.
/// </summary>
public class SvgMapRendererTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private static PlaceMap Map(uint seed, int scale = 5, bool water = false, int keys = 0) =>
        MapGenerator.Generate(
            new MapBrief
            {
                Shape = "hollow",
                Scale = scale,
                Subject = "a hollow oak",
                HasWater = water,
                Keys = [.. Enumerable.Range(1, keys).Select(i => new MapKeySubject($"Name {i}", $"Detail {i}"))]
            },
            seed);

    private static int PathsIn(XDocument document, string groupClass) =>
        document.Descendants(Svg + "g")
            .Where(g => (string?)g.Attribute("class") == groupClass)
            .Sum(g => g.Elements(Svg + "path").Count());

    [Fact]
    public void OutputIsWellFormedXml()
    {
        for (uint seed = 1; seed <= 30; seed++)
        {
            // Parsing is the real check: a malformed path or an unescaped label would throw here.
            XDocument document = XDocument.Parse(SvgMapRenderer.Render(Map(seed, 6, keys: 4), seed));

            Assert.Equal("svg", document.Root!.Name.LocalName);
        }
    }

    [Fact]
    public void OutputCarriesAViewBoxSoItScales()
    {
        PlaceMap map = Map(1234);
        XDocument document = XDocument.Parse(SvgMapRenderer.Render(map, 1234));

        Assert.Equal($"0 0 {map.Width} {map.Height}", document.Root!.Attribute("viewBox")!.Value);
    }

    [Fact]
    public void OutputIsAccessible()
    {
        PlaceMap map = Map(555);
        XElement root = XDocument.Parse(SvgMapRenderer.Render(map, 555)).Root!;

        Assert.Equal("img", root.Attribute("role")!.Value);
        Assert.Equal(map.Subject, root.Attribute("aria-label")!.Value);
    }

    [Fact]
    public void TheCallersLabelAndClassAreUsedWhenGiven()
    {
        XElement root = XDocument.Parse(SvgMapRenderer.Render(
            Map(555),
            555,
            new MapRenderOptions { AriaLabel = "Karte der Siedlung, hohle Eiche", CssClass = "settlement-map" })).Root!;

        Assert.Equal("Karte der Siedlung, hohle Eiche", root.Attribute("aria-label")!.Value);
        Assert.Equal("settlement-map", root.Attribute("class")!.Value);
    }

    [Fact]
    public void ALabelIsEscapedRatherThanInjected()
    {
        // A label reaches the engine already composed, and may have come from a hand-edited name.
        string svg = SvgMapRenderer.Render(
            Map(8, 4),
            8,
            new MapRenderOptions { AriaLabel = "The <script>alert(1)</script> & Bell" });

        XElement root = XDocument.Parse(svg).Root!;

        Assert.DoesNotContain("<script>", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("The <script>alert(1)</script> & Bell", root.Attribute("aria-label")!.Value);
    }

    [Fact]
    public void AKeyedBuildingsNameIsEscapedRatherThanInjected()
    {
        PlaceMap map = MapGenerator.Generate(
            new MapBrief
            {
                Shape = "hollow",
                Scale = 4,
                Subject = "a hollow oak",
                Keys = [new MapKeySubject("The <script>alert(1)</script> & Bell", "a detail")]
            },
            8);

        string svg = SvgMapRenderer.Render(map, 8);

        Assert.DoesNotContain("<script>", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            XDocument.Parse(svg).Descendants().Select(e => e.Value),
            value => value.Contains("alert(1)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The page wants a map that fills its frame, which a bare <c>viewBox</c> gives. A copy that
    /// leaves the page has to carry its own size instead: an image with no intrinsic dimensions is
    /// drawn at the default object size of 300×150, so the map would arrive as a thumbnail.
    /// </summary>
    [Fact]
    public void ASizeIsWrittenOnlyWhenItIsAskedFor()
    {
        PlaceMap map = Map(2468);

        XElement onPage = XDocument.Parse(SvgMapRenderer.Render(map, 2468)).Root!;
        XElement standalone = XDocument.Parse(SvgMapRenderer.Render(
            map, 2468, new MapRenderOptions { IntrinsicSize = true })).Root!;

        Assert.Null(onPage.Attribute("width"));
        Assert.Null(onPage.Attribute("height"));

        Assert.Equal(
            map.Width.ToString(CultureInfo.InvariantCulture), standalone.Attribute("width")!.Value);
        Assert.Equal(
            map.Height.ToString(CultureInfo.InvariantCulture), standalone.Attribute("height")!.Value);

        // The viewBox is unchanged, so the drawing inside is identical either way.
        Assert.Equal(onPage.Attribute("viewBox")!.Value, standalone.Attribute("viewBox")!.Value);
    }

    [Fact]
    public void EveryRoadAndBuildingIsDrawn()
    {
        PlaceMap map = Map(4321, 6);
        XDocument document = XDocument.Parse(SvgMapRenderer.Render(map, 4321));

        // Each road is stroked twice: a dark casing and a lighter fill.
        Assert.Equal(map.Roads.Count * 2, PathsIn(document, "map-roads"));

        // Each building is an outline plus a roof ridge.
        Assert.Equal(map.Buildings.Count * 2, PathsIn(document, "map-buildings"));
    }

    [Fact]
    public void KeyedBuildingsGetNumberedMarkers()
    {
        PlaceMap map = Map(31337, 6, keys: 5);
        XDocument document = XDocument.Parse(SvgMapRenderer.Render(map, 31337));

        string[] labels =
        [
            .. document.Descendants(Svg + "g")
                .Where(g => (string?)g.Attribute("class") == "map-keys")
                .SelectMany(g => g.Elements(Svg + "text"))
                .Select(t => t.Value)
        ];

        int keyed = map.Buildings.Count(b => b.IsKeyed);

        Assert.Equal(keyed, labels.Length);
        Assert.Equal(
            Enumerable.Range(1, keyed).Select(i => i.ToString(CultureInfo.InvariantCulture)).Order(),
            labels.Order());
    }

    [Fact]
    public void PathsUseInvariantNumberFormatting()
    {
        // A comma decimal separator under a European culture would corrupt every path.
        string svg = SvgMapRenderer.Render(Map(2024, 6), 2024);

        Assert.DoesNotContain(",,", svg, StringComparison.Ordinal);
        Assert.Matches(@"d=""M \d", svg);
    }

    [Fact]
    public void WaterIsDrawnOnlyWhenTheMapHasIt()
    {
        Assert.Contains("map-water", SvgMapRenderer.Render(Map(7, water: true), 7), StringComparison.Ordinal);
        Assert.DoesNotContain("map-water", SvgMapRenderer.Render(Map(7), 7), StringComparison.Ordinal);
    }

    [Fact]
    public void RenderingIsDeterministic()
    {
        PlaceMap map = Map(6161, 6, keys: 4);

        Assert.Equal(SvgMapRenderer.Render(map, 6161), SvgMapRenderer.Render(map, 6161));
    }

    [Fact]
    public void TheWobbleFollowsTheSeed()
    {
        PlaceMap map = Map(6161, 6);

        Assert.NotEqual(SvgMapRenderer.Render(map, 1), SvgMapRenderer.Render(map, 2));
    }

    [Fact]
    public void OutputStaysAReasonableSize()
    {
        // A city map that ran to megabytes would make the page and the print view unusable.
        Assert.InRange(SvgMapRenderer.Render(Map(11, 6), 11).Length, 2_000, 400_000);
    }
}
