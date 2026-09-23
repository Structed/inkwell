using System.Globalization;
using Structed.Inkwell.Dice;
using Structed.Inkwell.Party;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Messages arrive from other people's browsers, so the reader is the boundary.
/// </summary>
/// <remarks>
/// There is no server in front of this to sanitise anything. Whatever <see cref="RollMessage.TryRead"/>
/// accepts is what gets rendered, so these tests are mostly about what it refuses.
/// </remarks>
public sealed class RollMessageTests
{
    /// <summary>
    /// Stands in for whatever preset a game would have made this roll with.
    /// </summary>
    /// <remarks>
    /// Named here rather than borrowed from a real one, because nothing in this package knows any
    /// game. What the tests need is a preset id and a reading key that travel, and a handful of
    /// poker keys that must not — and a made-up set says that more plainly than a real one would.
    /// </remarks>
    private const string PresetId = "strike";

    private const string ReadingKey = "strike/minor";

    private static readonly string[] PokerKeys =
        ["poker/pair", "poker/triplet", "poker/poker", "poker/sequence", "poker/house"];

    private static RollMessage Sample(bool secret = false)
    {
        Assert.True(DiceNotation.TryParse("4d6kh3+1", out DiceNotation notation));

        return RollMessage.From(
            "roll-1",
            "Ada",
            DiceRolls.Roll(notation, 4242),
            new RollReading(ReadingKey, 1),
            secret,
            DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000),
            PresetId);
    }

    [Fact]
    public void ARollSurvivesTheRoundTrip()
    {
        RollMessage sent = Sample();

        Assert.True(RollMessage.TryRead(sent.Write(), out RollMessage read));

        Assert.Equal(sent.Id, read.Id);
        Assert.Equal(sent.Player, read.Player);
        Assert.Equal(sent.Notation, read.Notation);
        Assert.Equal(sent.Faces, read.Faces);
        Assert.Equal(sent.Kept, read.Kept);
        Assert.Equal(sent.Total, read.Total);
        Assert.Equal(sent.Seed, read.Seed);
        Assert.Equal(sent.ReadingKey, read.ReadingKey);
        Assert.Equal(sent.ReadingValue, read.ReadingValue);
        Assert.Equal(sent.Preset, read.Preset);
        Assert.Equal(sent.At, read.At);
        Assert.False(read.Secret);
    }

    /// <summary>
    /// The wire carries which preset was used and never what the dice added up to beyond the total.
    /// </summary>
    /// <remarks>
    /// Poker results are read again at the far end, from faces that have already been checked, so
    /// there is nothing here for a peer to lie about. Pinned down because sending them would be the
    /// obvious shortcut, and it would hand anybody on the relay a free result.
    /// </remarks>
    [Fact]
    public void NoPokerResultIsSentOnlyThePresetThatWouldReadThem()
    {
        string json = Sample().Write();

        Assert.Contains(PresetId, json, StringComparison.Ordinal);

        foreach (string key in PokerKeys)
        {
            Assert.DoesNotContain(key, json, StringComparison.Ordinal);
        }
    }

    /// <summary>A preset claimed from the wire is bounded like every other string.</summary>
    [Fact]
    public void ARidiculousPresetIsShortenedRatherThanRefused()
    {
        RollMessage sent = Sample() with { Preset = new string('h', 500) };

        Assert.True(RollMessage.TryRead(sent.Write(), out RollMessage read));
        Assert.True(read.Preset.Length <= RollMessage.MaximumTextLength);
    }

    /// <summary>
    /// A private roll has to leave nothing behind in what it sends.
    /// </summary>
    /// <remarks>
    /// Asserted against the serialised text rather than the object, because the object is not what
    /// travels. The strong form of the claim is that the bytes cannot depend on the hidden roll at
    /// all: a ghost of a roll that happened must be indistinguishable from a ghost of a roll that
    /// never did. Anything weaker leaves room for a field to survive that a listener could read,
    /// however carefully the page then avoided drawing it.
    /// </remarks>
    [Fact]
    public void AGhostCarriesNothingButTheFactThatSomebodyRolled()
    {
        RollMessage secret = Sample(secret: true);

        RollMessage nothingHappened = new()
        {
            Id = secret.Id,
            Player = secret.Player,
            Secret = true,
            At = secret.At
        };

        string json = secret.Ghost().Write();

        Assert.Equal(nothingHappened.Write(), json);
        Assert.DoesNotContain("4d6", json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret.Seed.ToString(CultureInfo.InvariantCulture), json, StringComparison.Ordinal);
        Assert.DoesNotContain("strike/minor", json, StringComparison.Ordinal);
        Assert.DoesNotContain(PresetId, json, StringComparison.Ordinal);

        Assert.True(RollMessage.TryRead(json, out RollMessage read));

        Assert.True(read.Secret);
        Assert.Equal("Ada", read.Player);
        Assert.Empty(read.Faces);
        Assert.Equal(0, read.Total);
        Assert.Equal(0u, read.Seed);
    }

    [Fact]
    public void GhostingTwiceChangesNothing()
    {
        RollMessage once = Sample(secret: true).Ghost();

        Assert.Equal(once, once.Ghost());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"version":1,"id":"a"}""")]
    [InlineData("""{"version":1,"player":"Ada"}""")]
    [InlineData("""{"version":2,"id":"a","player":"Ada","notation":"d6","faces":[3]}""")]
    [InlineData("""{"version":1,"id":"a","player":"Ada","notation":"d6","faces":[]}""")]
    [InlineData("""{"version":1,"id":"a","player":"Ada","faces":[3]}""")]
    [InlineData("""{"version":1,"id":"  ","player":"Ada","notation":"d6","faces":[3]}""")]
    public void AMalformedMessageIsDropped(string? json)
    {
        Assert.False(RollMessage.TryRead(json, out _));
    }

    [Fact]
    public void AMessageLongerThanAnyRealRollIsDropped()
    {
        string faces = string.Join(',', Enumerable.Repeat('3', 3000));
        string json = $$"""{"version":1,"id":"a","player":"Ada","notation":"d6","faces":[{{faces}}]}""";

        Assert.False(RollMessage.TryRead(json, out _));
    }

    [Fact]
    public void MoreDiceThanCanBeRolledAreDropped()
    {
        string faces = string.Join(',', Enumerable.Repeat(3, RollMessage.MaximumDice + 1));
        string json = $$"""{"version":1,"id":"a","player":"Ada","notation":"d6","faces":[{{faces}}]}""";

        Assert.False(RollMessage.TryRead(json, out _));
    }

    [Fact]
    public void KeepFlagsThatDoNotMatchTheDiceAreDropped()
    {
        Assert.False(RollMessage.TryRead(
            """{"version":1,"id":"a","player":"Ada","notation":"d6","faces":[3,4],"kept":[true]}""",
            out _));
    }

    [Fact]
    public void MissingKeepFlagsMeanEveryDieCounted()
    {
        Assert.True(RollMessage.TryRead(
            """{"version":1,"id":"a","player":"Ada","notation":"2d6","faces":[3,4]}""",
            out RollMessage read));

        Assert.Equal([true, true], read.Kept);
    }

    /// <summary>A name from the wire is a string somebody else chose.</summary>
    [Fact]
    public void ALongNameIsShortenedRatherThanRefused()
    {
        string json = $$"""
            {"version":1,"id":"a","player":"{{new string('x', 400)}}","notation":"d6","faces":[3]}
            """;

        Assert.True(RollMessage.TryRead(json, out RollMessage read));
        Assert.Equal(RollMessage.MaximumNameLength, read.Player.Length);
    }

    [Fact]
    public void ControlCharactersAreStrippedOutOfNames()
    {
        Assert.True(RollMessage.TryRead(
            """{"version":1,"id":"a","player":"A\u0000d\u001ba","notation":"d6","faces":[3]}""",
            out RollMessage read));

        Assert.Equal("Ada", read.Player);
    }

    /// <summary>
    /// Nothing in a message is trusted to be a real notation.
    /// </summary>
    /// <remarks>
    /// The notation is echoed, not re-rolled, so a peer sending <c>"not a roll"</c> is a display
    /// problem rather than an execution one — but it still has to arrive shortened and inert.
    /// </remarks>
    [Fact]
    public void ANotationFromTheWireIsTextAndNothingMore()
    {
        string json = $$"""
            {"version":1,"id":"a","player":"Ada","notation":"{{new string('d', 400)}}","faces":[3]}
            """;

        Assert.True(RollMessage.TryRead(json, out RollMessage read));
        Assert.Equal(RollMessage.MaximumTextLength, read.Notation.Length);
    }
}
