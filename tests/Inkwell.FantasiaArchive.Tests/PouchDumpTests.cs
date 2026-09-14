using System.Text.Json;

namespace Structed.Inkwell.Interop.FantasiaArchive.Tests;

/// <summary>
/// The dump format itself, with nothing generated behind it.
/// </summary>
/// <remarks>
/// Every assertion here is about a shape Fantasia Archive's loader insists on. None of it can be
/// checked by reading the app's source, which is why it is pinned in tests rather than trusted.
/// </remarks>
public class PouchDumpTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static Document Doc(
        string id = "doc-1", string type = "locations", params DocumentField[] fields) =>
        new()
        {
            Id = id,
            Type = type,
            Revision = "1-" + new string('a', 32),
            Fields = fields,
        };

    private static IReadOnlyList<string> Lines(string dump) =>
        dump.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void ADumpIsAHeaderThenTheDocumentsThenASequence()
    {
        string dump = PouchDump.Write("locations", [Doc(), Doc("doc-2")], Timestamp);

        IReadOnlyList<string> lines = Lines(dump);
        Assert.Equal(3, lines.Count);

        using JsonDocument header = JsonDocument.Parse(lines[0]);
        Assert.Equal("0.1.0", header.RootElement.GetProperty("version").GetString());
        Assert.Equal("idb", header.RootElement.GetProperty("db_type").GetString());

        JsonElement info = header.RootElement.GetProperty("db_info");
        Assert.Equal(2, info.GetProperty("doc_count").GetInt32());
        Assert.Equal(2, info.GetProperty("update_seq").GetInt32());
        Assert.Equal("locations", info.GetProperty("db_name").GetString());

        using JsonDocument batch = JsonDocument.Parse(lines[1]);
        Assert.Equal(2, batch.RootElement.GetProperty("docs").GetArrayLength());

        using JsonDocument tail = JsonDocument.Parse(lines[2]);
        Assert.Equal(2, tail.RootElement.GetProperty("seq").GetInt32());
    }

    [Fact]
    public void EveryLineEndsWithANewlineIncludingTheLast()
    {
        string dump = PouchDump.Write("locations", [Doc()], Timestamp);

        Assert.EndsWith("\n", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", dump, StringComparison.Ordinal);
        Assert.Equal(3, dump.Count(c => c == '\n'));
    }

    [Fact]
    public void ADumpIsNamedAfterTheDatabaseItLoadsInto()
    {
        Assert.Equal("locations.txt", PouchDump.FileNameFor("locations"));
        Assert.Equal(".txt", PouchDump.FileExtension);
    }

    [Fact]
    public void ADocumentDeclaresItsRevisionAsHistoryToo()
    {
        // Loaded with new_edits: false, a document without a matching _revisions block is dropped
        // silently — the failure mode that looks like a working export that imports nothing.
        string hash = new('b', 32);
        Document document = new()
        {
            Id = "doc-1",
            Type = "locations",
            Revision = "1-" + hash,
            Fields = [],
        };

        JsonElement written = Single(PouchDump.Write("locations", [document], Timestamp));

        Assert.Equal("1-" + hash, written.GetProperty("_rev").GetString());

        JsonElement revisions = written.GetProperty("_revisions");
        Assert.Equal(1, revisions.GetProperty("start").GetInt32());
        Assert.Equal(hash, Assert.Single(revisions.GetProperty("ids").EnumerateArray()).GetString());
    }

    [Fact]
    public void ADocumentCarriesItsIdUnderBothNames()
    {
        JsonElement written = Single(PouchDump.Write("locations", [Doc()], Timestamp));

        Assert.Equal("doc-1", written.GetProperty("_id").GetString());
        Assert.Equal("doc-1", written.GetProperty("id").GetString());
        Assert.Equal("locations", written.GetProperty("type").GetString());
    }

    [Fact]
    public void ADocumentCarriesTheNavigationItsBlueprintImplies()
    {
        JsonElement written = Single(PouchDump.Write("locations", [Doc()], Timestamp));

        Assert.Equal(FantasiaArchiveBlueprints.IconFor("locations"), written.GetProperty("icon").GetString());
        Assert.Equal(
            FantasiaArchiveBlueprints.HierarchicalPathFor("locations"),
            written.GetProperty("hierarchicalPath").GetString());
        Assert.Equal(
            FantasiaArchiveBlueprints.Url("locations", "doc-1"),
            written.GetProperty("url").GetString());
    }

    [Fact]
    public void FieldsKeepTheOrderTheyWereGivenIn()
    {
        Document document = Doc(
            fields:
            [
                new DocumentField("name", new TextValue("Owlmill")),
                new DocumentField("description", new TextValue("A mill.")),
                new DocumentField("tags", new StringsValue(["quiet"])),
            ]);

        JsonElement written = Single(PouchDump.Write("locations", [document], Timestamp));

        string[] ids = [.. written.GetProperty("extraFields").EnumerateArray()
            .Select(f => f.GetProperty("id").GetString()!)];

        Assert.Equal(["name", "description", "tags"], ids);
    }

    [Fact]
    public void ReadingBackFindsEveryDocument()
    {
        string dump = PouchDump.Write("locations", [Doc(), Doc("doc-2"), Doc("doc-3")], Timestamp);

        IReadOnlyList<JsonElement> read = PouchDump.ReadDocuments(dump);

        Assert.Equal(["doc-1", "doc-2", "doc-3"], read.Select(d => d.GetProperty("_id").GetString()));
    }

    [Fact]
    public void ReadingBackSurvivesLinesThatAreNotDocuments()
    {
        // A dump written by the app itself, or by a later version of it, may carry lines this
        // reader has never heard of. Skipping them beats refusing the file.
        string dump = string.Join('\n',
        [
            "{\"version\":\"0.1.0\"}",
            "not json at all",
            "",
            "[1,2,3]",
            "{\"docs\":\"not an array\"}",
            "{\"docs\":[{\"_id\":\"doc-1\"},\"not an object\"]}",
            "{\"seq\":1}",
        ]);

        JsonElement only = Assert.Single(PouchDump.ReadDocuments(dump));
        Assert.Equal("doc-1", only.GetProperty("_id").GetString());
    }

    [Fact]
    public void ReadingBackSurvivesWindowsLineEndings()
    {
        string dump = PouchDump.Write("locations", [Doc()], Timestamp).ReplaceLineEndings("\r\n");

        Assert.Single(PouchDump.ReadDocuments(dump));
    }

    [Fact]
    public void AStringFieldCanBeFishedOutAgain()
    {
        Document document = Doc(
            fields:
            [
                new DocumentField("name", new TextValue("Owlmill")),
                new DocumentField("tags", new StringsValue(["quiet"])),
            ]);

        JsonElement written = Single(PouchDump.Write("locations", [document], Timestamp));

        Assert.Equal("Owlmill", PouchDump.FieldString(written, "name"));

        // A field that is there but is not a string is not a string.
        Assert.Null(PouchDump.FieldString(written, "tags"));
        Assert.Null(PouchDump.FieldString(written, "absent"));
    }

    [Fact]
    public void FishingAFieldOutOfSomethingThatIsNotADocumentIsNotAnError()
    {
        using JsonDocument rubbish = JsonDocument.Parse("[1,2,3]");

        Assert.Null(PouchDump.FieldString(rubbish.RootElement, "name"));
    }

    [Fact]
    public void ADumpWithNoDocumentsIsStillAValidDump()
    {
        string dump = PouchDump.Write("locations", [], Timestamp);

        Assert.Equal(3, Lines(dump).Count);
        Assert.Empty(PouchDump.ReadDocuments(dump));
    }

    [Fact]
    public void ADumpNeedsADatabaseToLoadInto()
    {
        Assert.Throws<ArgumentException>(() => PouchDump.Write(" ", [Doc()], Timestamp));
        Assert.Throws<ArgumentNullException>(() => PouchDump.Write("locations", null!, Timestamp));
    }

    private static JsonElement Single(string dump) => Assert.Single(PouchDump.ReadDocuments(dump));
}
