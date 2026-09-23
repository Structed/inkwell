using System.Globalization;
using System.Text;
using Structed.Inkwell.Dice;

namespace Structed.Inkwell.Tests;

/// <summary>
/// A seed has to mean the same dice on every machine that ever reads it.
/// </summary>
/// <remarks>
/// The seed is the fallback when the peer-to-peer link will not form: it gets pasted into a chat
/// window and rebuilt by hand. That only works if it rebuilds the same roll, so this is checked the
/// same way the site baselines are — against a fixture, byte for byte.
/// </remarks>
public sealed class DiceRollTests
{
    private static DiceNotation Parse(string text)
    {
        Assert.True(DiceNotation.TryParse(text, out DiceNotation notation));
        return notation;
    }

    [Fact]
    public void TheSameSeedRollsTheSameDice()
    {
        DiceNotation notation = Parse("4d6kh3+1");

        RollOutcome first = DiceRolls.Roll(notation, 4242);
        RollOutcome second = DiceRolls.Roll(notation, 4242);

        Assert.Equal(first.Faces, second.Faces);
        Assert.Equal(first.Total, second.Total);
    }

    [Fact]
    public void EveryDieLandsOnTheDie()
    {
        DiceNotation notation = Parse("20d20");

        for (uint seed = 1; seed <= 200; seed++)
        {
            foreach (RolledDie die in DiceRolls.Roll(notation, seed).Dice)
            {
                Assert.InRange(die.Face, 1, 20);
            }
        }
    }

    [Fact]
    public void KeepingTheHighestDropsTheRest()
    {
        DiceNotation notation = Parse("5d6kh2");

        for (uint seed = 1; seed <= 100; seed++)
        {
            RollOutcome outcome = DiceRolls.Roll(notation, seed);

            Assert.Equal(5, outcome.Dice.Count);
            Assert.Equal(2, outcome.Dice.Count(die => die.IsKept));
            Assert.Equal(outcome.Dice.Where(die => die.IsKept).Sum(die => die.Face), outcome.Total);

            int lowestKept = outcome.Dice.Where(die => die.IsKept).Min(die => die.Face);
            int highestDropped = outcome.Dice.Where(die => !die.IsKept).Max(die => die.Face);

            Assert.True(lowestKept >= highestDropped);
        }
    }

    [Fact]
    public void KeepingTheLowestDoesTheOpposite()
    {
        RollOutcome outcome = DiceRolls.Roll(Parse("5d6kl2"), 77);

        int highestKept = outcome.Dice.Where(die => die.IsKept).Max(die => die.Face);
        int lowestDropped = outcome.Dice.Where(die => !die.IsKept).Min(die => die.Face);

        Assert.Equal(2, outcome.Dice.Count(die => die.IsKept));
        Assert.True(highestKept <= lowestDropped);
    }

    [Fact]
    public void TheModifierIsAddedOnceAndOnlyOnce()
    {
        RollOutcome plain = DiceRolls.Roll(Parse("3d6"), 11);
        RollOutcome modified = DiceRolls.Roll(Parse("3d6+4"), 11);

        Assert.Equal(plain.Faces, modified.Faces);
        Assert.Equal(plain.Total + 4, modified.Total);
    }

    [Fact]
    public void ANegativeModifierCanTakeATotalBelowZero()
    {
        RollOutcome outcome = DiceRolls.Roll(Parse("1d2-1000"), 3);

        Assert.True(outcome.Total < 0);
    }

    /// <summary>The seed code is what gets pasted, so it has to survive being read back.</summary>
    [Fact]
    public void TheSeedCodeRoundTrips()
    {
        foreach (uint seed in new uint[] { 0, 1, 42, 65535, uint.MaxValue })
        {
            RollOutcome outcome = DiceRolls.Roll(Parse("2d6"), seed);

            Assert.True(Structed.Inkwell.Randomness.SeedCodec.TryDecode(outcome.SeedCode, out uint read));
            Assert.Equal(seed, read);
        }
    }

    [Fact]
    public void ARolledSeedIsNotAlwaysTheSameSeed()
    {
        HashSet<uint> seeds = [];

        for (int attempt = 0; attempt < 50; attempt++)
        {
            seeds.Add(DiceRolls.CreateSeed());
        }

        Assert.True(seeds.Count > 40);
    }

    [Fact]
    public void TheDiceHaveNotMoved()
    {
        string[] notations = ["d20", "2d6", "3d6+2", "4d6kh3", "5d6kl2", "10d6", "1d100-5"];
        uint[] seeds = [1u, 7u, 42u, 1337u, 65535u];
        StringBuilder manifest = new();

        foreach (string text in notations)
        {
            foreach (uint seed in seeds)
            {
                RollOutcome outcome = DiceRolls.Roll(Parse(text), seed);

                manifest.Append(outcome.Notation.Text).Append('\t')
                    .Append(outcome.SeedCode).Append('\t')
                    .AppendJoin(
                        ',',
                        outcome.Dice.Select(die =>
                            die.IsKept
                                ? die.Face.ToString(CultureInfo.InvariantCulture)
                                : $"({die.Face.ToString(CultureInfo.InvariantCulture)})"))
                    .Append('\t')
                    .Append(outcome.Total)
                    .Append('\n');
            }
        }

        GoldenFile.Verify("dice-baseline.txt", manifest.ToString());
    }
}
