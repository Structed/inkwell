using System.IO.Compression;
using System.Text;

namespace Structed.Inkwell.Interop.FantasiaArchive.Tests;

/// <summary>
/// Packing an export into something a browser can hand over, and reading one back.
/// </summary>
public class FantasiaArchivePackageTests
{
    private static FantasiaArchiveExport Export(params ExportedFile[] files) =>
        new("owlmill-c21p6", files.Length > 0 ? files : [new ExportedFile("locations.txt", "{}\n")]);

    private static IReadOnlyList<string> EntryNames(byte[] zip)
    {
        using MemoryStream stream = new(zip);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        return [.. archive.Entries.Select(e => e.FullName)];
    }

    private static byte[] ContentOf(byte[] zip, string name)
    {
        using MemoryStream stream = new(zip);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        ZipArchiveEntry entry = Assert.Single(archive.Entries, e => e.FullName == name);

        using Stream content = entry.Open();
        using MemoryStream copy = new();
        content.CopyTo(copy);
        return copy.ToArray();
    }

    [Fact]
    public void EveryDumpGoesInsideTheProjectFolder()
    {
        byte[] zip = FantasiaArchivePackage.Create(
            Export(new ExportedFile("locations.txt", "a"), new ExportedFile("items.txt", "b")),
            "how to import");

        Assert.Contains("owlmill-c21p6/locations.txt", EntryNames(zip));
        Assert.Contains("owlmill-c21p6/items.txt", EntryNames(zip));
    }

    [Fact]
    public void TheInstructionsSitBesideTheFolderAndNeverInsideIt()
    {
        // Load-bearing. The app's merge feeds every file in the folder to its database loader, so
        // a readme among the dumps is parsed as a database and takes the import down.
        byte[] zip = FantasiaArchivePackage.Create(Export(), "how to import");

        Assert.Contains(FantasiaArchivePackage.ReadMeName, EntryNames(zip));
        Assert.DoesNotContain(
            EntryNames(zip),
            name => name.StartsWith("owlmill-c21p6/", StringComparison.Ordinal) &&
                name.EndsWith(FantasiaArchivePackage.ReadMeName, StringComparison.Ordinal));
    }

    [Fact]
    public void TheMapSitsBesideTheFolderForTheSameReason()
    {
        byte[] zip = FantasiaArchivePackage.Create(Export(), "how to import", [1, 2, 3]);

        Assert.Contains("owlmill-c21p6-map.png", EntryNames(zip));
        Assert.Equal([1, 2, 3], ContentOf(zip, "owlmill-c21p6-map.png"));
    }

    [Fact]
    public void AnArchiveWithoutAMapSimplyHasNoMapInIt()
    {
        Assert.DoesNotContain(
            EntryNames(FantasiaArchivePackage.Create(Export(), "how to import")),
            name => name.EndsWith(".png", StringComparison.Ordinal));

        Assert.DoesNotContain(
            EntryNames(FantasiaArchivePackage.Create(Export(), "how to import", [])),
            name => name.EndsWith(".png", StringComparison.Ordinal));
    }

    [Fact]
    public void FilesAreWrittenAsUtf8WithoutAByteOrderMark()
    {
        byte[] zip = FantasiaArchivePackage.Create(
            Export(new ExportedFile("locations.txt", "Mäusekönig")), "how to import");

        byte[] content = ContentOf(zip, "owlmill-c21p6/locations.txt");

        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], content[..3]);
        Assert.Equal("Mäusekönig", Encoding.UTF8.GetString(content));
    }

    [Fact]
    public void TheArchiveAndTheMapAreNamedAfterTheFolder()
    {
        FantasiaArchiveExport export = Export();

        Assert.Equal("owlmill-c21p6-fantasia-archive.zip", FantasiaArchivePackage.FileNameFor(export));

        // A sibling of the folder, never a child: no path separator in it.
        Assert.Equal("owlmill-c21p6-map.png", FantasiaArchivePackage.MapFileNameFor(export));
        Assert.DoesNotContain('/', FantasiaArchivePackage.MapFileNameFor(export));
    }

    [Fact]
    public void ExtractingFindsTheDumpsAndLeavesTheRestAlone()
    {
        byte[] zip = FantasiaArchivePackage.Create(
            Export(new ExportedFile("locations.txt", "a"), new ExportedFile("items.txt", "b")),
            "how to import",
            [1, 2, 3]);

        using MemoryStream stream = new(zip);
        IReadOnlyList<string> dumps = FantasiaArchivePackage.ExtractDumps(stream);

        Assert.Equal(["a", "b"], dumps);
    }

    [Fact]
    public void ExtractingFindsDumpsWhereverTheReaderPutThem()
    {
        // Someone who unzipped a project and zipped it up again, folder and all, still gets to
        // open it.
        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string path in new[] { "locations.txt", "deep/nested/items.txt" })
            {
                using Stream content = archive.CreateEntry(path).Open();
                using StreamWriter writer = new(content);
                writer.Write(path);
            }
        }

        stream.Position = 0;
        Assert.Equal(
            ["locations.txt", "deep/nested/items.txt"],
            FantasiaArchivePackage.ExtractDumps(stream));
    }

    [Fact]
    public void ExtractingIgnoresTheInstructionsEvenThoughTheyAreAlsoText()
    {
        byte[] zip = FantasiaArchivePackage.Create(Export(), "how to import");

        using MemoryStream stream = new(zip);
        Assert.DoesNotContain("how to import", FantasiaArchivePackage.ExtractDumps(stream));
    }

    [Fact]
    public void AnArchiveRoundTripsThroughItsOwnZip()
    {
        byte[] zip = FantasiaArchivePackage.Create(
            Export(new ExportedFile("locations.txt", "a")), "how to import", [1, 2, 3]);

        using MemoryStream stream = new(zip);
        Assert.Equal("a", Assert.Single(FantasiaArchivePackage.ExtractDumps(stream)));
    }

    [Fact]
    public void PackingNothingIsRefusedRatherThanGuessedAt()
    {
        Assert.Throws<ArgumentNullException>(
            () => FantasiaArchivePackage.Create(null!, "how to import"));
        Assert.Throws<ArgumentNullException>(() => FantasiaArchivePackage.Create(Export(), null!));
        Assert.Throws<ArgumentNullException>(() => FantasiaArchivePackage.ExtractDumps(null!));
    }
}
