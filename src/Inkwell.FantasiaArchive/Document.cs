using System.Text.Json;

namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>
/// One value on a Fantasia Archive document.
/// </summary>
/// <remarks>
/// Field values are genuinely polymorphic — a string, a list of strings, a boolean, a relationship
/// object or a list of labelled notes, depending on what the blueprint says the field is. Rather
/// than model that as <c>object</c> and hand it to a serialiser that would have to guess, each shape
/// writes itself.
/// </remarks>
public abstract record FieldValue
{
    internal abstract void Write(Utf8JsonWriter writer);

    /// <summary>An empty value, which the app writes as an empty string whatever the field's type.</summary>
    public static FieldValue Empty { get; } = new TextValue("");
}

/// <summary>A plain string, used for text fields and single-select keys.</summary>
public sealed record TextValue(string Value) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer) => writer.WriteStringValue(Value);
}

/// <summary>A number, used only where the blueprint really says number.</summary>
public sealed record NumberValue(int Value) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer) => writer.WriteNumberValue(Value);
}

/// <summary>A switch. Only ever written when true; an unset switch is an empty string.</summary>
public sealed record SwitchValue(bool Value) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer)
    {
        if (Value)
        {
            writer.WriteBooleanValue(true);
        }
        else
        {
            writer.WriteStringValue("");
        }
    }
}

/// <summary>A list of bare strings, used for tags and multi-select keys.</summary>
public sealed record StringsValue(IReadOnlyList<string> Values) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach (string value in Values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }
}

/// <summary>One entry in a list field, optionally labelled.</summary>
public sealed record ListEntry(string Value, string? Affix = null);

/// <summary>A list field, which renders as a run of optionally labelled notes.</summary>
public sealed record ListValue(IReadOnlyList<ListEntry> Entries) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach (ListEntry entry in Entries)
        {
            writer.WriteStartObject();
            writer.WriteString("value", entry.Value);
            if (entry.Affix is { Length: > 0 } affix)
            {
                writer.WriteString("affix", affix);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}

/// <summary>
/// The far end of a relationship.
/// </summary>
/// <param name="Id">The target document's id.</param>
/// <param name="Type">The target document's blueprint id.</param>
/// <param name="PairedField">
/// The field id on the <em>target</em> document that holds the other half of this relationship, or
/// empty for a one-directional relationship. The app uses this to find the field to update when the
/// document is next edited, so a wrong value here corrupts the pairing rather than merely mislinking
/// it.
/// </param>
public sealed record RelationshipTarget(string Id, string Type, string PairedField = "")
{
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("_id", Id);

        // The app is inconsistent with itself here: the interface declares `value`, while its own
        // repair routine writes `id`. Both are emitted so either reader finds what it expects.
        writer.WriteString("id", Id);
        writer.WriteString("value", Id);

        writer.WriteString("type", Type);
        writer.WriteString("url", FantasiaArchiveBlueprints.Url(Type, Id));
        writer.WriteString("pairedField", PairedField);
        writer.WriteEndObject();
    }
}

/// <summary>A relationship holding at most one target.</summary>
public sealed record SingleRelationshipValue(RelationshipTarget? Target) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("value");
        if (Target is null)
        {
            writer.WriteStringValue("");
        }
        else
        {
            Target.Write(writer);
        }

        writer.WriteString("addedValues", "");
        writer.WriteEndObject();
    }
}

/// <summary>A relationship holding any number of targets.</summary>
public sealed record ManyRelationshipValue(IReadOnlyList<RelationshipTarget> Targets) : FieldValue
{
    internal override void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        writer.WritePropertyName("value");
        writer.WriteStartArray();
        foreach (RelationshipTarget target in Targets)
        {
            target.Write(writer);
        }

        writer.WriteEndArray();

        writer.WritePropertyName("addedValues");
        writer.WriteStartArray();
        writer.WriteEndArray();

        writer.WriteEndObject();
    }
}

/// <summary>One entry in a document's field list.</summary>
public sealed record DocumentField(string Id, FieldValue Value);

/// <summary>
/// A Fantasia Archive document.
/// </summary>
/// <remarks>
/// <see cref="Revision"/> is not decoration. The app loads a dump through PouchDB's bulk writer with
/// <c>new_edits: false</c>, which refuses to invent revisions, so a document without one is silently
/// dropped. This is the single most likely way for an exporter like this to appear to work and
/// import nothing.
/// </remarks>
public sealed record Document
{
    public required string Id { get; init; }

    /// <summary>The blueprint id, which is also the file the document is written to.</summary>
    public required string Type { get; init; }

    /// <summary>A revision of the form <c>1-{32 lowercase hex}</c>.</summary>
    public required string Revision { get; init; }

    public required IReadOnlyList<DocumentField> Fields { get; init; }

    public string Icon => FantasiaArchiveBlueprints.IconFor(Type);

    public string HierarchicalPath => FantasiaArchiveBlueprints.HierarchicalPathFor(Type);

    public string Url => FantasiaArchiveBlueprints.Url(Type, Id);

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();

        writer.WriteString("_id", Id);
        writer.WriteString("_rev", Revision);

        // The revision has to be declared as history too, or the bulk writer rejects it.
        writer.WritePropertyName("_revisions");
        writer.WriteStartObject();
        writer.WriteNumber("start", 1);
        writer.WritePropertyName("ids");
        writer.WriteStartArray();
        writer.WriteStringValue(RevisionHash);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteString("id", Id);
        writer.WriteString("type", Type);
        writer.WriteString("icon", Icon);
        writer.WriteString("url", Url);
        writer.WriteString("hierarchicalPath", HierarchicalPath);

        writer.WritePropertyName("extraFields");
        writer.WriteStartArray();
        foreach (DocumentField field in Fields)
        {
            writer.WriteStartObject();
            writer.WriteString("id", field.Id);
            writer.WritePropertyName("value");
            field.Value.Write(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private string RevisionHash =>
        Revision.IndexOf('-') is int dash && dash >= 0 ? Revision[(dash + 1)..] : Revision;
}
