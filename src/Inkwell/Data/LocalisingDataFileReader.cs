using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Structed.Inkwell.Data;

/// <summary>
/// Applies a language's translation overlay to every data file as it is read.
/// </summary>
/// <remarks>
/// Written as a decorator over another <see cref="IDataFileReader"/> so that translation is
/// invisible to whatever consumes the data: the loader, the validator and every generator go on
/// seeing a single well-formed data file. The browser and the tests each keep their own way of
/// fetching bytes.
/// </remarks>
public sealed class LocalisingDataFileReader(IDataFileReader inner, Locale locale) : IDataFileReader
{
    /// <summary>
    /// The leniency a data file is parsed with, which matches what the source-generated
    /// deserialiser is configured with.
    /// </summary>
    /// <remarks>
    /// Public because anyone checking their own data files — a translation validator, an importer —
    /// has to parse them exactly as this reader will. Keeping a second copy of these options
    /// somewhere else is how a checker comes to accept a file the loader then rejects.
    /// </remarks>
    public static JsonDocumentOptions DocumentOptions { get; } = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly IDataFileReader _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Locale _locale = locale ?? throw new ArgumentNullException(nameof(locale));

    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        Stream canonical = await _inner.OpenAsync(relativePath, cancellationToken);

        if (_locale.IsCanonical)
        {
            return canonical;
        }

        JsonNode? merged;
        await using (canonical)
        {
            JsonNode? baseNode = await ParseAsync(canonical, cancellationToken);
            JsonNode? overlayNode = await ReadOverlayAsync(relativePath, cancellationToken);

            merged = JsonOverlay.Merge(baseNode, overlayNode);
        }

        return new MemoryStream(Encoding.UTF8.GetBytes(merged?.ToJsonString() ?? "null"));
    }

    private async Task<JsonNode?> ReadOverlayAsync(string relativePath, CancellationToken cancellationToken)
    {
        string overlayPath = $"{_locale.OverlayRoot}/{relativePath}";

        try
        {
            await using Stream overlay = await _inner.OpenAsync(overlayPath, cancellationToken);
            return await ParseAsync(overlay, cancellationToken);
        }
        catch (Exception ex) when (ex is not GameDataException and not OperationCanceledException)
        {
            throw new GameDataException(
                $"The {_locale.EnglishName} translation file '{overlayPath}' could not be read: {ex.Message}. " +
                "Every data file needs a translation in every language the app ships.",
                ex);
        }
    }

    private static async Task<JsonNode?> ParseAsync(Stream stream, CancellationToken cancellationToken) =>
        await JsonNode.ParseAsync(stream, nodeOptions: null, DocumentOptions, cancellationToken);
}
