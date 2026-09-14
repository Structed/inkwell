using System.Text.Json;

namespace Structed.Inkwell.Interop.FantasiaArchive.Tests;

/// <summary>
/// The payload that rides along on a document so a generated thing can be regenerated.
/// </summary>
public class HiddenStateTests
{
    private const string FormatId = "example-tool/state";
    private const string PayloadName = "place";
    private const string Payload = """{"format":"example-tool/place","version":1,"seed":4242}""";

    [Fact]
    public void WhatGoesInComesBackOut()
    {
        string state = HiddenState.Write(FormatId, "doc-1", PayloadName, Payload);

        string? read = HiddenState.Read(FormatId, PayloadName, state);

        Assert.NotNull(read);
        using JsonDocument parsed = JsonDocument.Parse(read);
        Assert.Equal(4242, parsed.RootElement.GetProperty("seed").GetInt32());
    }

    [Fact]
    public void ThePayloadIsNestedAsAnObjectRatherThanEscapedAsAString()
    {
        // So anyone who does go looking at the field finds something readable.
        using JsonDocument parsed =
            JsonDocument.Parse(HiddenState.Write(FormatId, "doc-1", PayloadName, Payload));

        Assert.Equal(JsonValueKind.Object, parsed.RootElement.GetProperty(PayloadName).ValueKind);
    }

    [Fact]
    public void TheStateRecordsWhoseItIsAndWhichDocumentItRodeIn()
    {
        using JsonDocument parsed =
            JsonDocument.Parse(HiddenState.Write(FormatId, "doc-1", PayloadName, Payload));

        Assert.Equal(FormatId, parsed.RootElement.GetProperty("format").GetString());

        // Recorded so a document duplicated inside the app can be told from the original.
        Assert.Equal("doc-1", parsed.RootElement.GetProperty("documentId").GetString());
    }

    [Fact]
    public void SomebodyElsesStateIsNotRead()
    {
        string state = HiddenState.Write("other-tool/state", "doc-1", PayloadName, Payload);

        Assert.Null(HiddenState.Read(FormatId, PayloadName, state));
    }

    [Fact]
    public void StateUnderADifferentPayloadNameIsNotRead()
    {
        string state = HiddenState.Write(FormatId, "doc-1", "somethingElse", Payload);

        Assert.Null(HiddenState.Read(FormatId, PayloadName, state));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("{}")]
    [InlineData("""{"format":"example-tool/state"}""")]
    [InlineData("""{"format":"example-tool/state","place":"not an object"}""")]
    public void RubbishIsRefusedRatherThanCrashing(string? state)
    {
        Assert.Null(HiddenState.Read(FormatId, PayloadName, state));
    }

    [Fact]
    public void WritingNothingIsRefusedRatherThanWrittenAsNothing()
    {
        Assert.Throws<ArgumentException>(
            () => HiddenState.Write(" ", "doc-1", PayloadName, Payload));
        Assert.Throws<ArgumentException>(() => HiddenState.Write(FormatId, " ", PayloadName, Payload));
        Assert.Throws<ArgumentException>(() => HiddenState.Write(FormatId, "doc-1", " ", Payload));
        Assert.Throws<ArgumentException>(
            () => HiddenState.Write(FormatId, "doc-1", PayloadName, " "));
    }

    [Fact]
    public void ReadingNeedsToKnowWhatItIsLookingFor()
    {
        Assert.Throws<ArgumentException>(() => HiddenState.Read(" ", PayloadName, "{}"));
        Assert.Throws<ArgumentException>(() => HiddenState.Read(FormatId, " ", "{}"));
    }
}
