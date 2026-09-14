using System.Text.Json;

namespace Structed.Inkwell.Interop.FantasiaArchive.Tests;

/// <summary>
/// How each kind of field value writes itself.
/// </summary>
/// <remarks>
/// A field is polymorphic and the app has a blueprint saying which shape it expects, so a value
/// written as the wrong shape does not fail loudly — it lands in the document and renders as
/// nothing.
/// </remarks>
public class FieldValueTests
{
    private static JsonElement Write(FieldValue value)
    {
        Document document = new()
        {
            Id = "doc-1",
            Type = FantasiaArchiveBlueprints.Locations,
            Revision = "1-" + new string('a', 32),
            Fields = [new DocumentField("field", value)],
        };

        string dump = PouchDump.Write(
            FantasiaArchiveBlueprints.Locations, [document], DateTimeOffset.UnixEpoch);

        JsonElement written = Assert.Single(PouchDump.ReadDocuments(dump));
        return Assert.Single(written.GetProperty("extraFields").EnumerateArray())
            .GetProperty("value");
    }

    [Fact]
    public void TextIsAString()
    {
        JsonElement value = Write(new TextValue("Owlmill"));

        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.Equal("Owlmill", value.GetString());
    }

    [Fact]
    public void ANumberIsANumber()
    {
        JsonElement value = Write(new NumberValue(42));

        Assert.Equal(JsonValueKind.Number, value.ValueKind);
        Assert.Equal(42, value.GetInt32());
    }

    [Fact]
    public void AnEmptyValueIsAnEmptyStringWhateverTheFieldIs()
    {
        JsonElement value = Write(FieldValue.Empty);

        Assert.Equal(JsonValueKind.String, value.ValueKind);
        Assert.Equal("", value.GetString());
    }

    [Fact]
    public void ASwitchIsOnlyABooleanWhenItIsOn()
    {
        Assert.Equal(JsonValueKind.True, Write(new SwitchValue(true)).ValueKind);

        // An unset switch is an empty string, not false: that is how the app writes its own.
        JsonElement off = Write(new SwitchValue(false));
        Assert.Equal(JsonValueKind.String, off.ValueKind);
        Assert.Equal("", off.GetString());
    }

    [Fact]
    public void StringsAreABareArray()
    {
        JsonElement value = Write(new StringsValue(["quiet", "damp"]));

        Assert.Equal(["quiet", "damp"], value.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void AnEmptyStringListIsStillAnArray()
    {
        JsonElement value = Write(new StringsValue([]));

        Assert.Equal(JsonValueKind.Array, value.ValueKind);
        Assert.Equal(0, value.GetArrayLength());
    }

    [Fact]
    public void AListEntryIsAnObjectAndItsLabelIsOptional()
    {
        JsonElement value = Write(new ListValue(
        [
            new ListEntry("20p", "Price"),
            new ListEntry("A plain note"),
            new ListEntry("Also plain", ""),
        ]));

        JsonElement[] entries = [.. value.EnumerateArray()];
        Assert.Equal(3, entries.Length);

        Assert.Equal("20p", entries[0].GetProperty("value").GetString());
        Assert.Equal("Price", entries[0].GetProperty("affix").GetString());

        // An absent label is absent, not an empty one.
        Assert.False(entries[1].TryGetProperty("affix", out _));
        Assert.False(entries[2].TryGetProperty("affix", out _));
    }

    [Fact]
    public void ARelationshipTargetCarriesItsIdUnderEveryNameTheAppReadsIt()
    {
        // The app's interface declares `value` while its own repair routine writes `id`, so both
        // are emitted; `_id` is what PouchDB itself matches on.
        JsonElement value = Write(new SingleRelationshipValue(
            new RelationshipTarget("target-1", FantasiaArchiveBlueprints.Characters, "pairedField")));

        JsonElement target = value.GetProperty("value");
        Assert.Equal("target-1", target.GetProperty("_id").GetString());
        Assert.Equal("target-1", target.GetProperty("id").GetString());
        Assert.Equal("target-1", target.GetProperty("value").GetString());
        Assert.Equal(FantasiaArchiveBlueprints.Characters, target.GetProperty("type").GetString());
        Assert.Equal(
            FantasiaArchiveBlueprints.Url(FantasiaArchiveBlueprints.Characters, "target-1"),
            target.GetProperty("url").GetString());
        Assert.Equal("pairedField", target.GetProperty("pairedField").GetString());
    }

    [Fact]
    public void AOneDirectionalRelationshipNamesNoPairedField()
    {
        JsonElement value = Write(new SingleRelationshipValue(
            new RelationshipTarget("target-1", FantasiaArchiveBlueprints.Locations)));

        Assert.Equal("", value.GetProperty("value").GetProperty("pairedField").GetString());
    }

    [Fact]
    public void AnEmptySingleRelationshipIsAnEmptyStringNotNull()
    {
        JsonElement value = Write(new SingleRelationshipValue(null));

        Assert.Equal(JsonValueKind.String, value.GetProperty("value").ValueKind);
        Assert.Equal("", value.GetProperty("value").GetString());

        // addedValues is a string here and an array on a many-relationship. That asymmetry is the
        // app's, not ours.
        Assert.Equal(JsonValueKind.String, value.GetProperty("addedValues").ValueKind);
    }

    [Fact]
    public void AManyRelationshipIsAnArrayOfTargetsBesideAnEmptyAddedValues()
    {
        JsonElement value = Write(new ManyRelationshipValue(
        [
            new RelationshipTarget("a", FantasiaArchiveBlueprints.Items, "pairedField"),
            new RelationshipTarget("b", FantasiaArchiveBlueprints.Items, "pairedField"),
        ]));

        Assert.Equal(
            ["a", "b"],
            value.GetProperty("value").EnumerateArray().Select(t => t.GetProperty("_id").GetString()));

        Assert.Equal(JsonValueKind.Array, value.GetProperty("addedValues").ValueKind);
        Assert.Equal(0, value.GetProperty("addedValues").GetArrayLength());
    }

    [Fact]
    public void AnEmptyManyRelationshipIsAnEmptyArrayRatherThanAnEmptyString()
    {
        JsonElement value = Write(new ManyRelationshipValue([]));

        Assert.Equal(JsonValueKind.Array, value.GetProperty("value").ValueKind);
        Assert.Equal(0, value.GetProperty("value").GetArrayLength());
    }
}
