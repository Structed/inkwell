using System.Text.RegularExpressions;

namespace Structed.Inkwell.Interop.FantasiaArchive.Tests;

/// <summary>
/// The ids and revisions a document is given.
/// </summary>
/// <remarks>
/// These are derived, not drawn, so that exporting the same thing twice is a no-op on a second
/// merge rather than a duplicate set.
/// </remarks>
public class DocumentIdentityTests
{
    [Fact]
    public void TheSameSeedAndPathAlwaysGiveTheSameIdentity()
    {
        (string Id, string Revision) first = DocumentIdentity.For(4242, "settlement");
        (string Id, string Revision) second = DocumentIdentity.For(4242, "settlement");

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADifferentPathUnderOneSeedIsADifferentDocument()
    {
        Assert.NotEqual(
            DocumentIdentity.For(4242, "settlement"),
            DocumentIdentity.For(4242, "settlement/shop/0"));
    }

    [Fact]
    public void ADifferentSeedUnderOnePathIsADifferentDocument()
    {
        Assert.NotEqual(
            DocumentIdentity.For(4242, "settlement"),
            DocumentIdentity.For(4243, "settlement"));
    }

    [Fact]
    public void AnIdIsAVersionFourUuid()
    {
        // Not cosmetic: the app mints its own ids this way and its repair tooling recognises
        // documents by the shape, so the version and variant bits have to be right.
        (string id, _) = DocumentIdentity.For(4242, "settlement");

        Assert.Matches(
            new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$"),
            id);
    }

    [Fact]
    public void ARevisionIsTheFirstGenerationAndThirtyTwoHexDigits()
    {
        (_, string revision) = DocumentIdentity.For(4242, "settlement");

        Assert.Matches(new Regex("^1-[0-9a-f]{32}$"), revision);
    }

    [Fact]
    public void AnIdAndItsRevisionAreNotTheSameBytes()
    {
        (string id, string revision) = DocumentIdentity.For(4242, "settlement");

        Assert.NotEqual(id.Replace("-", "", StringComparison.Ordinal), revision["1-".Length..]);
    }

    [Fact]
    public void ManyPathsUnderOneSeedDoNotCollide()
    {
        HashSet<string> ids =
        [
            .. Enumerable.Range(0, 500)
                .Select(i => DocumentIdentity.For(4242, $"settlement/shop/{i}").Id),
        ];

        Assert.Equal(500, ids.Count);
    }

    [Fact]
    public void ADocumentNeedsAPathToBeIdentifiedBy()
    {
        Assert.Throws<ArgumentException>(() => DocumentIdentity.For(4242, ""));
        Assert.Throws<ArgumentException>(() => DocumentIdentity.For(4242, " "));
        Assert.Throws<ArgumentNullException>(() => DocumentIdentity.For(4242, null!));
    }
}
