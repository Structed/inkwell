using Structed.Inkwell.Party;

namespace Structed.Inkwell.Tests;

/// <summary>
/// A hail is a name from somebody else's browser, so the reader is the boundary.
/// </summary>
/// <remarks>
/// It arrives earlier than a roll does — before anybody has done anything — which makes it the
/// first thing a stranger at the table can put on your screen. These tests are mostly about what
/// it refuses.
/// </remarks>
public sealed class HailTests
{
    [Fact]
    public void ANameSurvivesTheRoundTrip()
    {
        Hail sent = Hail.From("Ada", DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000));

        Assert.True(Hail.TryRead(sent.Write(), out Hail read));

        Assert.Equal("Ada", read.Player);
        Assert.Equal(sent.At, read.At);
        Assert.Equal(Hail.CurrentVersion, read.Version);
    }

    /// <summary>
    /// A hail says who somebody is and nothing about where they are sitting.
    /// </summary>
    /// <remarks>
    /// Which connection a hail arrived on is the transport's word for it. If the message carried
    /// its own peer id, one player could rename another by claiming theirs, so the field that would
    /// let them is not there to be trusted in the first place.
    /// </remarks>
    [Fact]
    public void AHailCarriesNothingButANameAndAClock()
    {
        string json = Hail.From("Ada", DateTimeOffset.UnixEpoch).Write();

        Assert.Contains("Ada", json, StringComparison.Ordinal);
        Assert.DoesNotContain("peer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("id", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"player":""}""")]
    [InlineData("""{"version":1,"player":"   "}""")]
    [InlineData("""{"version":2,"player":"Ada"}""")]
    public void AMalformedHailIsDropped(string? json)
    {
        Assert.False(Hail.TryRead(json, out _));
    }

    [Fact]
    public void AHailLongerThanANameCouldExplainIsDropped()
    {
        string json = $$"""{"version":1,"player":"{{new string('x', Hail.MaximumBytes)}}"}""";

        Assert.False(Hail.TryRead(json, out _));
    }

    /// <summary>A name from the wire is a string somebody else chose.</summary>
    [Fact]
    public void ALongNameIsShortenedRatherThanRefused()
    {
        string json = $$"""{"version":1,"player":"{{new string('x', 400)}}"}""";

        Assert.True(Hail.TryRead(json, out Hail read));
        Assert.Equal(RollMessage.MaximumNameLength, read.Player.Length);
    }

    [Fact]
    public void ControlCharactersAreStrippedOutOfNames()
    {
        Assert.True(Hail.TryRead("""{"version":1,"player":"A\u0000d\u001ba"}""", out Hail read));

        Assert.Equal("Ada", read.Player);
    }

    /// <summary>The same rules apply on the way out as on the way in.</summary>
    [Fact]
    public void AHailWrittenHereIsCleanedBeforeItIsSent()
    {
        Hail sent = Hail.From("  " + new string('x', 400) + "  ", DateTimeOffset.UnixEpoch);

        Assert.Equal(RollMessage.MaximumNameLength, sent.Player.Length);
    }

    [Fact]
    public void ANamelessHailIsNotWorthSending()
    {
        Hail sent = Hail.From("   ", DateTimeOffset.UnixEpoch);

        Assert.Equal("", sent.Player);
        Assert.False(Hail.TryRead(sent.Write(), out _));
    }
}
