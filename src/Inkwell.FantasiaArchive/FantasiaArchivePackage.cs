using System.IO.Compression;
using System.Text;

namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>
/// Packs an export into a ZIP, and unpacks one again.
/// </summary>
/// <remarks>
/// <para>
/// Fantasia Archive imports a <em>folder</em>, and a page running in a browser cannot hand one over.
/// A ZIP holding that folder is the nearest thing: the reader unpacks it and points the app at what
/// falls out.
/// </para>
/// <para>
/// The instructions sit outside the project folder, not in it, and that placement is load-bearing.
/// The app's merge reads every file in the folder it is given and feeds each one to its database
/// loader, so a readme sitting alongside the dumps would be parsed as a database and break the
/// import. The map image is kept out for the same reason.
/// </para>
/// </remarks>
public static class FantasiaArchivePackage
{
    /// <summary>The instructions, which deliberately live beside the folder rather than inside it.</summary>
    public const string ReadMeName = "HOW-TO-IMPORT.txt";

    /// <summary>Builds the downloadable archive.</summary>
    /// <param name="export">The project folder's contents.</param>
    /// <param name="readMe">The instructions.</param>
    /// <param name="mapPng">
    /// The subject's map as a PNG, or null if it could not be rasterised.
    /// </param>
    /// <remarks>
    /// The map goes beside the folder for the same reason the instructions do, and rather more
    /// urgently: a file in the folder is read as a database and takes the whole import down with it.
    /// It is a PNG rather than the SVG the site itself offers because Fantasia Archive's own image
    /// picker only lists <c>jpg</c>, <c>png</c>, <c>gif</c> and <c>webp</c>, so an SVG is not merely
    /// awkward to attach — it is not shown at all.
    /// </remarks>
    public static byte[] Create(
        FantasiaArchiveExport export, string readMe, byte[]? mapPng = null)
    {
        ArgumentNullException.ThrowIfNull(export);
        ArgumentNullException.ThrowIfNull(readMe);

        using MemoryStream stream = new();

        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ExportedFile file in export.Files)
            {
                Write(archive, $"{export.FolderName}/{file.Name}", file.Content);
            }

            Write(archive, ReadMeName, readMe);

            if (mapPng is { Length: > 0 })
            {
                ZipArchiveEntry entry =
                    archive.CreateEntry(MapFileNameFor(export), CompressionLevel.Optimal);

                using Stream content = entry.Open();
                content.Write(mapPng);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The archive's file name, e.g. <c>owlmill-c21p6-fantasia-archive.zip</c>.</summary>
    public static string FileNameFor(FantasiaArchiveExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        return $"{export.FolderName}-fantasia-archive.zip";
    }

    /// <summary>
    /// The map's name inside the archive, e.g. <c>owlmill-c21p6-map.png</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not a path: it sits at the archive's root, a sibling of the project folder and
    /// never a child of it.
    /// </remarks>
    public static string MapFileNameFor(FantasiaArchiveExport export)
    {
        ArgumentNullException.ThrowIfNull(export);

        return $"{export.FolderName}-map.png";
    }

    /// <summary>
    /// Pulls the database dumps out of an archive.
    /// </summary>
    /// <remarks>
    /// Takes anything that looks like a dump wherever it sits in the archive, so a project the
    /// reader has rezipped themselves, folder and all, still opens.
    /// </remarks>
    public static IReadOnlyList<string> ExtractDumps(Stream zip)
    {
        ArgumentNullException.ThrowIfNull(zip);

        List<string> dumps = [];

        using ZipArchive archive = new(zip, ZipArchiveMode.Read);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(PouchDump.FileExtension, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, ReadMeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using Stream content = entry.Open();
            using StreamReader reader = new(content, Encoding.UTF8);
            dumps.Add(reader.ReadToEnd());
        }

        return dumps;
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);

        using Stream stream = entry.Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
