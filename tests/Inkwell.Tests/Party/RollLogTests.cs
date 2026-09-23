using Structed.Inkwell.Party;

namespace Structed.Inkwell.Tests;

/// <summary>
/// The log has to absorb a replayed history without duplicating it or leaking through it.
/// </summary>
public sealed class RollLogTests
{
    private static RollMessage Roll(string id, string player = "Ada", bool secret = false, long at = 0) =>
        new()
        {
            Id = id,
            Player = player,
            Secret = secret,
            Notation = secret ? "" : "2d6",
            Faces = secret ? [] : [3, 4],
            Kept = secret ? [] : [true, true],
            Total = secret ? 0 : 7,
            Seed = secret ? 0u : 99u,
            At = at
        };

    [Fact]
    public void TheNewestRollIsAtTheTop()
    {
        RollLog log = new();

        log.Add(Roll("a"));
        log.Add(Roll("b"));

        Assert.Equal(["b", "a"], log.Entries.Select(entry => entry.Message.Id));
    }

    [Fact]
    public void TheSameRollArrivingTwiceIsStillOneRoll()
    {
        RollLog log = new();

        Assert.True(log.Add(Roll("a")));
        Assert.False(log.Add(Roll("a")));
        Assert.Single(log.Entries);
    }

    [Fact]
    public void ARollWithNoIdIsNotKept()
    {
        RollLog log = new();

        Assert.False(log.Add(Roll("")));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void OnlyRollsMadeHereAreMine()
    {
        RollLog log = new();

        log.Add(Roll("mine"), isMine: true);
        log.Add(Roll("theirs"));

        Assert.True(log.Entries.Single(entry => entry.Message.Id == "mine").IsMine);
        Assert.False(log.Entries.Single(entry => entry.Message.Id == "theirs").IsMine);
    }

    [Fact]
    public void AReplayedHistoryMergesRatherThanDuplicates()
    {
        RollLog log = new();

        log.Add(Roll("a", at: 1));
        log.Add(Roll("b", at: 2));

        int added = log.Merge([Roll("a", at: 1), Roll("b", at: 2), Roll("c", at: 3)]);

        Assert.Equal(1, added);
        Assert.Equal(3, log.Entries.Count);
    }

    /// <summary>A batch has to end up in the same order as a trickle would have done.</summary>
    [Fact]
    public void AMergedBatchLandsOldestFirst()
    {
        RollLog log = new();

        log.Merge([Roll("c", at: 3), Roll("a", at: 1), Roll("b", at: 2)]);

        Assert.Equal(["c", "b", "a"], log.Entries.Select(entry => entry.Message.Id));
    }

    [Fact]
    public void TheOldestRollsFallOffTheEnd()
    {
        RollLog log = new();

        for (int index = 0; index < RollLog.Capacity + 50; index++)
        {
            log.Add(Roll($"roll-{index}"));
        }

        Assert.Equal(RollLog.Capacity, log.Entries.Count);
        Assert.Equal("roll-249", log.Entries[0].Message.Id);
        Assert.DoesNotContain(log.Entries, entry => entry.Message.Id == "roll-0");
    }

    /// <summary>
    /// A roll that fell off the end may come round again without being treated as a duplicate.
    /// </summary>
    /// <remarks>
    /// Forgetting the id along with the entry keeps the memory bounded too — an id set that only
    /// ever grew would be a slow leak dressed up as a correctness guarantee.
    /// </remarks>
    [Fact]
    public void AForgottenRollCanComeBack()
    {
        RollLog log = new();

        for (int index = 0; index < RollLog.Capacity; index++)
        {
            log.Add(Roll($"roll-{index}"));
        }

        log.Add(Roll("newer"));

        Assert.True(log.Add(Roll("roll-0")));
    }

    /// <summary>
    /// A private roll visible on the roller's own screen must never travel on replay.
    /// </summary>
    [Fact]
    public void AReplayNeverCarriesSomebodyElsesPrivateDice()
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

        RollMessage replayed = Assert.Single(log.Replay());

        Assert.True(replayed.Secret);
        Assert.Empty(replayed.Faces);
        Assert.Equal("", replayed.Notation);
        Assert.Equal(0, replayed.Total);
        Assert.Equal(0u, replayed.Seed);
    }

    /// <summary>The roller keeps their own dice; only the outgoing copy is emptied.</summary>
    [Fact]
    public void ThePrivateRollerStillSeesTheirOwnDice()
    {
        RollLog log = new();

        log.Add(
            new RollMessage { Id = "secret", Player = "Me", Secret = true, Faces = [1, 1], Total = 2 },
            isMine: true);

        _ = log.Replay();

        Assert.Equal([1, 1], log.Entries[0].Message.Faces);
    }

    [Fact]
    public void AReplayIsOldestFirstAndBounded()
    {
        RollLog log = new();

        for (int index = 0; index < 100; index++)
        {
            log.Add(Roll($"roll-{index}", at: index));
        }

        IReadOnlyList<RollMessage> replay = log.Replay();

        Assert.Equal(RollLog.ReplayCount, replay.Count);
        Assert.Equal("roll-60", replay[0].Id);
        Assert.Equal("roll-99", replay[^1].Id);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    public void ANonsensicalReplayLengthAsksForNothing(int count)
    {
        RollLog log = new();
        log.Add(Roll("a"));

        Assert.Empty(log.Replay(count));
    }

    [Fact]
    public void TheLogSaysWhenItChanges()
    {
        RollLog log = new();
        int changes = 0;

        log.Changed += () => changes++;

        log.Add(Roll("a"));
        log.Add(Roll("a"));
        log.Clear();
        log.Clear();

        Assert.Equal(2, changes);
    }
}
