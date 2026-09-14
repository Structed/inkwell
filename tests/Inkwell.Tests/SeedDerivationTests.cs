using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Tests;

public class SeedDerivationTests
{
    [Fact]
    public void StringHashIsStableAcrossCalls()
    {
        // Guards against anyone swapping in string.GetHashCode, which is randomised per process
        // and would produce a different settlement on every page load.
        Assert.Equal(SeedDerivation.HashString("shop/3/quirk"), SeedDerivation.HashString("shop/3/quirk"));
    }

    [Fact]
    public void KnownStringHashesDoNotDrift()
    {
        // Canonical FNV-1a 64 reference values, pinned so the hash can never be silently
        // swapped out for one that is not stable across processes or runtimes.
        Assert.Equal(0xcbf29ce484222325UL, SeedDerivation.HashString(""));
        Assert.Equal(0xaf63dc4c8601ec8cUL, SeedDerivation.HashString("a"));
        Assert.Equal(0x85944171f73967e8UL, SeedDerivation.HashString("foobar"));
    }

    [Fact]
    public void DifferentPathsProduceDifferentStreams()
    {
        uint seed = 4242;

        Assert.NotEqual(
            SeedDerivation.Derive(seed, "settlement/name"),
            SeedDerivation.Derive(seed, "settlement/event"));
    }

    [Fact]
    public void DifferentRootSeedsProduceDifferentStreams()
    {
        const string path = "settlement/name";

        Assert.NotEqual(SeedDerivation.Derive(1, path), SeedDerivation.Derive(2, path));
    }

    [Fact]
    public void FieldStreamsAreIndependentOfSiblings()
    {
        // The point of sub-stream seeding: re-rolling shop 2 must not disturb shops 1 and 3.
        uint seed = 777;

        int[] Sample(string path)
        {
            var dice = new DiceRoller(SeedDerivation.CreateStream(seed, path));
            return [.. Enumerable.Range(0, 10).Select(_ => dice.Roll(20))];
        }

        int[] firstBefore = Sample("shop/1");
        int[] thirdBefore = Sample("shop/3");

        _ = Sample("shop/2");

        Assert.Equal(firstBefore, Sample("shop/1"));
        Assert.Equal(thirdBefore, Sample("shop/3"));
    }

    [Fact]
    public void AdjacentSeedsDoNotProduceCorrelatedFirstRolls()
    {
        // Without the SplitMix64 mix, seeds 1..N would yield a near-identical first value.
        var firstRolls = new HashSet<int>();
        for (uint seed = 1; seed <= 200; seed++)
        {
            var dice = new DiceRoller(SeedDerivation.CreateStream(seed, "settlement/size"));
            firstRolls.Add(dice.Roll(20));
        }

        Assert.True(firstRolls.Count > 10, $"Expected a spread of first rolls, saw {firstRolls.Count}.");
    }
}
