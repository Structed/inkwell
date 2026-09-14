using System.Text;
using System.Text.Json;

namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>
/// Reads and writes a Fantasia Archive database dump.
/// </summary>
/// <remarks>
/// <para>
/// A Fantasia Archive project is a folder of these, one per document type, and the file's name is
/// what decides which database it is loaded into. The format is newline-delimited JSON: a header
/// line, one or more lines carrying documents, and a trailing sequence line.
/// </para>
/// <para>
/// Written by hand rather than serialised from a model, because the field values inside a document
/// are polymorphic and the exact shape is the whole contract.
/// </para>
/// </remarks>
public static class PouchDump
{
    /// <summary>The extension every dump in a project folder carries.</summary>
    public const string FileExtension = ".txt";

    /// <summary>The dump format's own version, which is not the app's version.</summary>
    private const string DumpVersion = "0.1.0";

    /// <summary>The file a dump of <paramref name="type"/> must be written to.</summary>
    public static string FileNameFor(string type) => type + FileExtension;

    /// <summary>Writes a set of documents as one dump.</summary>
    public static string Write(
        string databaseName, IReadOnlyList<Document> documents, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(documents);

        StringBuilder builder = new();

        builder.Append(WriteLine(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("version", DumpVersion);

            // The app writes its projects out of the browser's IndexedDB. The loader ignores this,
            // but a dump that claims to be something it could not have been is needless noise.
            writer.WriteString("db_type", "idb");
            writer.WriteString("start_time", timestamp.UtcDateTime.ToString("o"));

            writer.WritePropertyName("db_info");
            writer.WriteStartObject();
            writer.WriteNumber("doc_count", documents.Count);
            writer.WriteNumber("update_seq", documents.Count);
            writer.WriteString("db_name", databaseName);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }));

        builder.Append(WriteLine(writer =>
        {
            writer.WriteStartObject();
            writer.WritePropertyName("docs");
            writer.WriteStartArray();
            foreach (Document document in documents)
            {
                document.Write(writer);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }));

        builder.Append(WriteLine(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("seq", documents.Count);
            writer.WriteEndObject();
        }));

        return builder.ToString();
    }

    /// <summary>
    /// Reads every document out of a dump.
    /// </summary>
    /// <remarks>
    /// Deliberately forgiving. The file may have been written by Fantasia Archive itself, by a
    /// different version of it, or by hand, so a line that is not a document batch is skipped rather
    /// than treated as an error.
    /// </remarks>
    public static IReadOnlyList<JsonElement> ReadDocuments(string dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        List<JsonElement> documents = [];

        foreach (string line in dump.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            JsonDocument parsed;
            try
            {
                parsed = JsonDocument.Parse(trimmed);
            }
            catch (JsonException)
            {
                continue;
            }

            using (parsed)
            {
                if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
                    !parsed.RootElement.TryGetProperty("docs", out JsonElement docs) ||
                    docs.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement document in docs.EnumerateArray())
                {
                    if (document.ValueKind == JsonValueKind.Object)
                    {
                        // Cloned so it outlives the JsonDocument it was parsed from.
                        documents.Add(document.Clone());
                    }
                }
            }
        }

        return documents;
    }

    /// <summary>Reads a string-valued entry from a document's field list, if it has one.</summary>
    public static string? FieldString(JsonElement document, string fieldId)
    {
        if (document.ValueKind != JsonValueKind.Object ||
            !document.TryGetProperty("extraFields", out JsonElement fields) ||
            fields.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement field in fields.EnumerateArray())
        {
            if (field.ValueKind == JsonValueKind.Object &&
                field.TryGetProperty("id", out JsonElement id) &&
                id.ValueKind == JsonValueKind.String &&
                string.Equals(id.GetString(), fieldId, StringComparison.Ordinal) &&
                field.TryGetProperty("value", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string WriteLine(Action<Utf8JsonWriter> write)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            write(writer);
        }

        // Newline-delimited, so every line ends with one, including the last.
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}
