namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>One file in an exported project folder.</summary>
public sealed record ExportedFile(string Name, string Content);

/// <summary>Something rendered as a Fantasia Archive project folder.</summary>
/// <param name="FolderName">The folder the files belong in, which is what the user points the app at.</param>
/// <param name="Files">The database dumps, one per document type.</param>
/// <param name="MapSvg">
/// The map, as it was embedded in the subject's own document.
/// </param>
/// <remarks>
/// The map is exposed as well as embedded so a caller that can rasterise it — which in practice
/// means one with a browser to hand — can package a copy Fantasia Archive is willing to accept
/// through its own image button. It is the very same drawing either way, rather than a second one
/// rendered from a seed that might have been derived differently.
/// </remarks>
public sealed record FantasiaArchiveExport(
    string FolderName, IReadOnlyList<ExportedFile> Files, string MapSvg = "");
