using System.Text;
using System.Text.Json;

namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>
/// A payload hidden on a Fantasia Archive document.
/// </summary>
/// <remarks>
/// <para>
/// Fantasia Archive has nowhere to keep a seed, and a generated place cannot be recovered from its
/// prose: a generator is not invertible. So the state that regenerates it has to travel with the
/// document, in a field the app has no blueprint for and therefore never shows, edits or discards.
/// </para>
/// <para>
/// The format id and the payload's name are the caller's, and both are frozen the moment a file is
/// written: they are what tells one tool's state from another's, and what a reader looks under.
/// </para>
/// </remarks>
public static class HiddenState
{
    /// <summary>Wraps an export for the journey.</summary>
    /// <param name="formatId">Marks the payload as the caller's, so a field collision is not read as theirs.</param>
    /// <param name="documentId">The Fantasia Archive document this was written onto.</param>
    /// <param name="payloadName">The property the payload is nested under.</param>
    /// <param name="payloadJson">The caller's own export format, verbatim.</param>
    public static string Write(
        string formatId, string documentId, string payloadName, string payloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadName);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", formatId);

            // Recorded so a document duplicated inside Fantasia Archive can be told from the
            // original, which would otherwise leave two documents claiming the same subject.
            writer.WriteString("documentId", documentId);

            // Nested as an object rather than as an escaped string, so anyone who does go looking
            // finds something readable.
            writer.WritePropertyName(payloadName);
            writer.WriteRawValue(payloadJson);

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Unwraps a payload, returning the export inside it.
    /// </summary>
    /// <returns>The embedded document's JSON, or <c>null</c> if this is not one of the caller's.</returns>
    public static string? Read(string formatId, string payloadName, string? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadName);

        if (state is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(state);

            if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                !parsed.RootElement.TryGetProperty("format", out JsonElement format) ||
                format.ValueKind != JsonValueKind.String ||
                !string.Equals(format.GetString(), formatId, StringComparison.Ordinal) ||
                !parsed.RootElement.TryGetProperty(payloadName, out JsonElement payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return payload.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
