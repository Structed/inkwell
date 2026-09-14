using System.Text.Json.Nodes;
using Structed.Inkwell.Data;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers merging a translation overlay onto a canonical data file.
/// </summary>
/// <remarks>
/// The shape checks are the point. Every roll is an index into a table, so an overlay that changes
/// a table's length would quietly make the same seed produce something different in each language.
/// Failing loudly at merge time is the only place that can still be caught cheaply.
/// </remarks>
public class JsonOverlayTests
{
    private static JsonNode? Merge(string canonical, string overlay) =>
        JsonOverlay.Merge(JsonNode.Parse(canonical), JsonNode.Parse(overlay));

    private static string MergeToString(string canonical, string overlay) =>
        Merge(canonical, overlay)?.ToJsonString() ?? "null";

    [Fact]
    public void ReplacesAValue() =>
        Assert.Equal("""{"name":"Hollenbruck"}""", MergeToString("""{"name":"Hollowbridge"}""", """{"name":"Hollenbruck"}"""));

    [Fact]
    public void LeavesUntouchedPropertiesAlone() =>
        Assert.Equal(
            """{"id":"tavern","label":"Wirtshaus"}""",
            MergeToString("""{"id":"tavern","label":"Tavern"}""", """{"label":"Wirtshaus"}"""));

    [Fact]
    public void AnExplicitNullKeepsTheCanonicalValue() =>
        Assert.Equal("""{"label":"Tavern"}""", MergeToString("""{"label":"Tavern"}""", """{"label":null}"""));

    [Fact]
    public void AddsAPropertyTheCanonicalFileDoesNotHave() =>
        Assert.Equal(
            """{"label":"Rose","gender":"f"}""",
            MergeToString("""{"label":"Rose"}""", """{"gender":"f"}"""));

    [Fact]
    public void MergesArraysByPosition() =>
        Assert.Equal("""["eins","two","drei"]""", MergeToString("""["one","two","three"]""", """["eins",null,"drei"]"""));

    [Fact]
    public void MergesNestedStructures() =>
        Assert.Equal(
            """{"table":{"rows":[{"text":"Fest"},{"text":"Flood"}]}}""",
            MergeToString(
                """{"table":{"rows":[{"text":"Feast"},{"text":"Flood"}]}}""",
                """{"table":{"rows":[{"text":"Fest"},null]}}"""));

    [Fact]
    public void RejectsAnArrayThatChangedLength()
    {
        GameDataException ex = Assert.Throws<GameDataException>(
            () => Merge("""{"rows":["one","two","three"]}""", """{"rows":["eins","zwei"]}"""));

        Assert.Contains("$.rows", ex.Message);
        Assert.Contains("2 entries", ex.Message);
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void RejectsAValueThatBecameAnObject()
    {
        GameDataException ex = Assert.Throws<GameDataException>(
            () => Merge("""{"label":"Tavern"}""", """{"label":{"de":"Wirtshaus"}}"""));

        Assert.Contains("$.label", ex.Message);
    }

    [Fact]
    public void RejectsAnObjectThatBecameAValue() =>
        Assert.Throws<GameDataException>(() => Merge("""{"text":{"a":"b"}}""", """{"text":"flat"}"""));

    [Fact]
    public void RejectsAnArrayThatBecameAValue() =>
        Assert.Throws<GameDataException>(() => Merge("""{"rows":["a"]}""", """{"rows":"a"}"""));

    [Fact]
    public void ReportsThePathOfANestedMismatch()
    {
        GameDataException ex = Assert.Throws<GameDataException>(
            () => Merge("""{"table":{"rows":["a","b"]}}""", """{"table":{"rows":["a"]}}"""));

        Assert.Contains("$.table.rows", ex.Message);
    }

    [Fact]
    public void AnAbsentOverlayLeavesTheCanonicalFileIntact() =>
        Assert.Equal("""{"label":"Tavern"}""", JsonOverlay.Merge(JsonNode.Parse("""{"label":"Tavern"}"""), null)?.ToJsonString());
}
