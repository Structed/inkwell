using Structed.Inkwell.Dice;
using Structed.Inkwell.Party;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Catching a newcomer up has to be as strict as telling them about one roll.
/// </summary>
public sealed class RollHistoryTests
{
    private static RollMessage Roll(string id, bool secret = false) =>
        new()
        {
            Id = id,
            Player = "Ada",
            Secret = secret,
            Notation = secret ? "" : "2d6",
            Faces = secret ? [] : [3, 4],
            Kept = secret ? [] : [true, true],
            Total = secret ? 0 : 7,
            At = 1
        };

    [Fact]
    public void AHistorySurvivesTheRoundTrip()
    {
        IReadOnlyList<RollMessage> read = RollHistory.Read(
            RollHistory.Write([Roll("a"), Roll("b"), Roll("c", secret: true)]));

        Assert.Equal(["a", "b", "c"], read.Select(message => message.Id));
        Assert.True(read[2].Secret);
    }

    [Fact]
    public void AnEmptyHistoryIsStillAHistory()
    {
        Assert.Empty(RollHistory.Read(RollHistory.Write([])));
    }

    /// <summary>
    /// Every roll in a batch goes through the same reader as a roll arriving alone.
    /// </summary>
    /// <remarks>
    /// The whole reason a history carries strings rather than objects. A laxer second path into the
    /// log is the sort of thing that is safe the day it is written and a hole a year later.
    /// </remarks>
    [Fact]
    public void ABadRollInABatchLosesThatRollAndNotTheBatch()
    {
        string json = """["{\"version\":1,\"id\":\"a\",\"player\":\"Ada\",\"notation\":\"d6\",\"faces\":[3]}","nonsense","{}"]""";

        RollMessage kept = Assert.Single(RollHistory.Read(json));

        Assert.Equal("a", kept.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[1,2,3]")]
    public void AMalformedBatchIsSimplyEmpty(string? json)
    {
        Assert.Empty(RollHistory.Read(json));
    }

    [Fact]
    public void ABatchLongerThanAnEveningIsRefused()
    {
        string json = "[" + string.Join(',', Enumerable.Repeat("\"" + new string('x', 200) + "\"", 500)) + "]";

        Assert.True(json.Length > RollHistory.MaximumBytes);
        Assert.Empty(RollHistory.Read(json));
    }

    [Fact]
    public void MoreRollsThanTheLimitAreTrimmedOnTheWayOut()
    {
        IEnumerable<RollMessage> many =
            Enumerable.Range(0, RollHistory.MaximumCount + 40).Select(index => Roll($"roll-{index}"));

        Assert.Equal(RollHistory.MaximumCount, RollHistory.Read(RollHistory.Write(many)).Count);
    }

    /// <summary>The log is what decides what may be told; this only has to carry it faithfully.</summary>
    [Fact]
    public void APrivateRollGhostedByTheLogStaysGhostedThroughTheBatch()
    {
        RollLog log = new();

        log.Add(
            new RollMessage
            {
                Id = "secret",
                Player = "Gamesmaster",
                Secret = true,
                Notation = "2d6",
                Faces = [1, 1],
                Kept = [true, true],
                Total = 2,
                Seed = 5
            },
            isMine: true);

        string json = RollHistory.Write(log.Replay());

        Assert.DoesNotContain("2d6", json, StringComparison.Ordinal);

        RollMessage read = Assert.Single(RollHistory.Read(json));

        Assert.True(read.Secret);
        Assert.Empty(read.Faces);
    }
}
