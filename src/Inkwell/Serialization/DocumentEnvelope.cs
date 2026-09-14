using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Structed.Inkwell.Serialization;

/// <summary>
/// Reads exported documents, and recognises them before reading.
/// </summary>
/// <remarks>
/// The generator's own types stay in the app that owns them, along with its source-generated
/// serializer context; what lives here is the fiddly part that every format gets wrong the same
/// way — a stray byte-order mark, a file from another tool, a file from a later version of this
/// one.
/// </remarks>
public static class DocumentEnvelope
{
    /// <summary>
    /// Reads a document and checks that it is one of ours and not from the future.
    /// </summary>
    /// <param name="json">The file's text.</param>
    /// <param name="formatId">The format id the caller expects to find.</param>
    /// <param name="currentVersion">The newest format version the caller can read.</param>
    /// <param name="typeInfo">
    /// The caller's source-generated type info, so that reading stays trimmable and AOT-safe.
    /// </param>
    /// <exception cref="DocumentFormatException">The file is not a readable export.</exception>
    public static T Read<T>(string json, string formatId, int currentVersion, JsonTypeInfo<T> typeInfo)
        where T : IGeneratedDocument
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        ArgumentNullException.ThrowIfNull(typeInfo);

        T? document;
        try
        {
            document = JsonSerializer.Deserialize(WithoutByteOrderMark(json), typeInfo);
        }
        catch (JsonException ex)
        {
            throw new DocumentFormatException($"That does not look like valid JSON: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new DocumentFormatException("The file was empty.");
        }

        if (!string.Equals(document.Format, formatId, StringComparison.Ordinal))
        {
            throw new DocumentFormatException(
                $"Expected a '{formatId}' file but found '{document.Format}'.");
        }

        if (document.Version > currentVersion)
        {
            throw new DocumentFormatException(
                $"That file was written by a newer version of this tool (format version {document.Version}).");
        }

        return document;
    }

    /// <summary>
    /// Whether some text is an export of the given format.
    /// </summary>
    /// <remarks>
    /// Used to tell an export of ours from another tool's file when the reader simply picks
    /// something and expects it to open. Deliberately parses the whole text as one document, so a
    /// newline-delimited file that happens to carry an embedded export of ours is not mistaken for
    /// a bare one.
    /// </remarks>
    public static bool Matches(string? text, string formatId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);

        if (text is not { Length: > 0 })
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(WithoutByteOrderMark(text));

            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("format", out JsonElement format) &&
                format.ValueKind == JsonValueKind.String &&
                string.Equals(format.GetString(), formatId, StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reduces a generated name to something safe to put in a filename.
    /// </summary>
    /// <param name="name">The name to reduce, which may be any script or empty.</param>
    /// <param name="fallback">What to call it when nothing usable survives.</param>
    public static string Slug(string? name, string fallback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        string slug = new(
        [
            .. (name ?? "")
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '-')
        ]);

        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));

        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary>
    /// Drops a leading byte-order mark.
    /// </summary>
    /// <remarks>
    /// A mark is not JSON, and an editor that saves one would otherwise turn a perfectly good export
    /// into an unreadable file. Reading through a <see cref="StreamReader"/> would strip it, but a
    /// caller holding the text has already passed that point.
    /// </remarks>
    private static string WithoutByteOrderMark(string text) => text.TrimStart('\uFEFF');
}
