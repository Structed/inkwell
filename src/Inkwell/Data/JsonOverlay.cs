using System.Text.Json.Nodes;

namespace Structed.Inkwell.Data;

/// <summary>
/// Merges a translation overlay onto a canonical data file.
/// </summary>
/// <remarks>
/// <para>
/// Translations are applied to the raw JSON before deserialisation rather than to the loaded
/// models, so every generator, validator and test keeps working on one set of types and the
/// loading path stays single.
/// </para>
/// <para>
/// Arrays merge by position, and a length mismatch is an error rather than something to paper
/// over. Every roll in this project is an index into a table, so a German table with a different
/// number of rows than its English original would quietly turn the same seed into a different
/// result and break every shared link.
/// </para>
/// </remarks>
public static class JsonOverlay
{
    /// <summary>
    /// Returns <paramref name="baseNode"/> with <paramref name="overlayNode"/> merged into it.
    /// </summary>
    /// <remarks>
    /// The base node is modified in place and returned. A <c>null</c> in the overlay means "keep
    /// what the canonical file says", which is how an entry that needs no translation — a proper
    /// noun, or a row that reads the same in both languages — is spelt.
    /// </remarks>
    /// <exception cref="GameDataException">
    /// The two trees disagree in shape: an array changed length, or a value became an object.
    /// </exception>
    public static JsonNode? Merge(JsonNode? baseNode, JsonNode? overlayNode) =>
        Merge(baseNode, overlayNode, "$");

    private static JsonNode? Merge(JsonNode? baseNode, JsonNode? overlayNode, string path)
    {
        // An absent or null overlay entry leaves the canonical value in place.
        if (overlayNode is null)
        {
            return baseNode;
        }

        if (baseNode is null)
        {
            return overlayNode.DeepClone();
        }

        return (baseNode, overlayNode) switch
        {
            (JsonObject baseObject, JsonObject overlayObject) => MergeObject(baseObject, overlayObject, path),
            (JsonArray baseArray, JsonArray overlayArray) => MergeArray(baseArray, overlayArray, path),
            (JsonObject, _) => throw Mismatch(path, "an object", overlayNode),
            (JsonArray, _) => throw Mismatch(path, "an array", overlayNode),
            (_, JsonObject or JsonArray) => throw Mismatch(path, "a value", overlayNode),
            _ => overlayNode.DeepClone()
        };
    }

    /// <summary>
    /// Merges property by property.
    /// </summary>
    /// <remarks>
    /// A property the canonical file does not have is added rather than rejected. Translations
    /// legitimately carry grammatical metadata that English has no use for, such as the gender of
    /// a shop-sign noun or a host object's complete prepositional phrase. The cost is that a
    /// mistyped key does nothing instead of failing here, which is what
    /// <c>TranslationCompletenessTests</c> exists to catch.
    /// </remarks>
    private static JsonNode MergeObject(JsonObject baseObject, JsonObject overlayObject, string path)
    {
        foreach (KeyValuePair<string, JsonNode?> property in overlayObject)
        {
            JsonNode? existing = baseObject[property.Key];
            JsonNode? merged = Merge(existing, property.Value, $"{path}.{property.Key}");

            // Re-assigning a node that is already in this slot would re-parent it and throw.
            if (!ReferenceEquals(existing, merged))
            {
                baseObject[property.Key] = merged;
            }
        }

        return baseObject;
    }

    private static JsonNode MergeArray(JsonArray baseArray, JsonArray overlayArray, string path)
    {
        if (baseArray.Count != overlayArray.Count)
        {
            throw new GameDataException(
                $"The translation at '{path}' has {overlayArray.Count} entries but the canonical " +
                $"table has {baseArray.Count}. Tables are rolled on by position, so they must line " +
                "up exactly or the same seed would produce a different result in each language.");
        }

        for (int i = 0; i < baseArray.Count; i++)
        {
            JsonNode? existing = baseArray[i];
            JsonNode? merged = Merge(existing, overlayArray[i], $"{path}[{i}]");

            if (!ReferenceEquals(existing, merged))
            {
                baseArray[i] = merged;
            }
        }

        return baseArray;
    }

    private static GameDataException Mismatch(string path, string expected, JsonNode overlayNode) =>
        new($"The translation at '{path}' is {Describe(overlayNode)}, but the canonical file has " +
            $"{expected} there. A translation may replace text; it may not change the shape of a table.");

    private static string Describe(JsonNode node) => node switch
    {
        JsonObject => "an object",
        JsonArray => "an array",
        _ => "a value"
    };
}
