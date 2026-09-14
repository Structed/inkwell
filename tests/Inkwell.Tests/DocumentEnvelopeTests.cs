using System.Text.Json;
using System.Text.Json.Serialization;
using Structed.Inkwell.Serialization;

namespace Structed.Inkwell.Tests;

internal sealed record TestDocument : IGeneratedDocument
{
    public string? Format { get; init; }

    public int Version { get; init; }

    public string? Payload { get; init; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestDocument))]
internal sealed partial class TestDocumentContext : JsonSerializerContext;

/// <summary>
/// Covers recognising and reading an exported document.
/// </summary>
public class DocumentEnvelopeTests
{
    private const string FormatId = "inkwell-tests/thing";

    private static TestDocument Read(string json) =>
        DocumentEnvelope.Read(json, FormatId, 2, TestDocumentContext.Default.TestDocument);

    [Fact]
    public void AWellFormedDocumentIsRead()
    {
        TestDocument document = Read(
            $$"""{ "format": "{{FormatId}}", "version": 2, "payload": "hello" }""");

        Assert.Equal(2, document.Version);
        Assert.Equal("hello", document.Payload);
    }

    [Fact]
    public void AnOlderVersionIsStillRead() =>
        Assert.Equal(1, Read($$"""{ "format": "{{FormatId}}", "version": 1 }""").Version);

    [Fact]
    public void ALeadingByteOrderMarkIsToleratedRatherThanFatal()
    {
        // An editor that saves one would otherwise turn a perfectly good export into a broken file.
        TestDocument document = Read("\uFEFF" + $$"""{ "format": "{{FormatId}}", "version": 2 }""");

        Assert.Equal(2, document.Version);
    }

    [Fact]
    public void AnotherToolsFileIsRejectedByName()
    {
        DocumentFormatException error = Assert.Throws<DocumentFormatException>(
            () => Read("""{ "format": "some-other-tool/thing", "version": 1 }"""));

        Assert.Contains(FormatId, error.Message, StringComparison.Ordinal);
        Assert.Contains("some-other-tool/thing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileWithNoFormatAtAllIsRejected() =>
        Assert.Throws<DocumentFormatException>(() => Read("""{ "version": 1 }"""));

    [Fact]
    public void AFileFromTheFutureIsRejectedRatherThanHalfRead()
    {
        DocumentFormatException error = Assert.Throws<DocumentFormatException>(
            () => Read($$"""{ "format": "{{FormatId}}", "version": 99 }"""));

        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SomethingThatIsNotJsonIsRejectedWithAReadableMessage()
    {
        DocumentFormatException error =
            Assert.Throws<DocumentFormatException>(() => Read("not json at all"));

        Assert.IsType<JsonException>(error.InnerException);
    }

    [Fact]
    public void AJsonNullIsRejected() =>
        Assert.Throws<DocumentFormatException>(() => Read("null"));

    [Fact]
    public void AnEmptyStringIsRejectedBeforeParsing() =>
        Assert.Throws<ArgumentException>(() => Read("   "));

    [Fact]
    public void OurOwnFileIsRecognised() =>
        Assert.True(DocumentEnvelope.Matches($$"""{ "format": "{{FormatId}}" }""", FormatId));

    [Fact]
    public void AMarkedFileIsStillRecognised() =>
        Assert.True(DocumentEnvelope.Matches("\uFEFF" + $$"""{ "format": "{{FormatId}}" }""", FormatId));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "format": "another-tool/thing" }""")]
    [InlineData("""{ "format": 7 }""")]
    [InlineData("""[ { "format": "inkwell-tests/thing" } ]""")]
    [InlineData("\"just a string\"")]
    public void AnythingElseIsNot(string? text) => Assert.False(DocumentEnvelope.Matches(text, FormatId));

    [Fact]
    public void AnEmbeddedExportIsNotMistakenForABareOne()
    {
        // A newline-delimited file carrying one of ours is not itself one of ours.
        string lines = $$"""{ "format": "{{FormatId}}" }""" + "\n" + $$"""{ "format": "{{FormatId}}" }""";

        Assert.False(DocumentEnvelope.Matches(lines, FormatId));
    }

    [Theory]
    [InlineData("Owlmill", "owlmill")]
    [InlineData("The Pitcher & Bell", "the-pitcher-bell")]
    [InlineData("  Spaced   Out  ", "spaced-out")]
    [InlineData("Nußschale", "nußschale")]
    [InlineData("MiXeD CaSe", "mixed-case")]
    public void ANameBecomesAFilenameSafeSlug(string name, string expected) =>
        Assert.Equal(expected, DocumentEnvelope.Slug(name, "fallback"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("---")]
    [InlineData("!?&")]
    public void ANameWithNothingUsableFallsBack(string? name) =>
        Assert.Equal("fallback", DocumentEnvelope.Slug(name, "fallback"));
}
