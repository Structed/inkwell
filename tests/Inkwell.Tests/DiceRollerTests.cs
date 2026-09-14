using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Tests;

public class DiceRollerTests
{
    private static DiceRoller Roller(uint seed = 1) => new(new Pcg32(seed));

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public void RollStaysWithinDieFaces(int sides)
    {
        DiceRoller dice = Roller();

        for (int i = 0; i < 2_000; i++)
        {
            Assert.InRange(dice.Roll(sides), 1, sides);
        }
    }

    [Fact]
    public void RollCoversEveryFace()
    {
        DiceRoller dice = Roller();
        var seen = new HashSet<int>();

        for (int i = 0; i < 2_000; i++)
        {
            seen.Add(dice.Roll(20));
        }

        Assert.Equal(20, seen.Count);
    }

    [Fact]
    public void RollSumStaysWithinBounds()
    {
        DiceRoller dice = Roller();

        for (int i = 0; i < 1_000; i++)
        {
            Assert.InRange(dice.RollSum(2, 6), 2, 12);
        }
    }

    [Fact]
    public void LowestOfTwoStaysWithinDieFaces()
    {
        DiceRoller dice = Roller();

        for (int i = 0; i < 2_000; i++)
        {
            Assert.InRange(dice.RollLowestOfTwo(6), 1, 6);
        }
    }

    [Fact]
    public void LowestOfTwoIsBiasedTowardSmallSettlements()
    {
        // Mausritter rolls 2d6 and keeps the lower value on purpose. For 2d6-take-lowest the
        // expected value is 91/36 which is a little over 2.5; a fair d6 would average 3.5.
        DiceRoller dice = Roller(42);
        const int trials = 40_000;

        double mean = Enumerable.Range(0, trials).Sum(_ => dice.RollLowestOfTwo(6)) / (double)trials;

        Assert.InRange(mean, 2.4, 2.7);
    }

    [Fact]
    public void LowestOfTwoFavoursOneOverSix()
    {
        DiceRoller dice = Roller(11);
        var counts = new int[7];

        for (int i = 0; i < 40_000; i++)
        {
            counts[dice.RollLowestOfTwo(6)]++;
        }

        Assert.True(counts[1] > counts[6] * 5, "Expected 1 to be far more common than 6.");
    }

    [Fact]
    public void PickDistinctReturnsRequestedCountWithoutRepeats()
    {
        DiceRoller dice = Roller();
        int[] source = [.. Enumerable.Range(0, 20)];

        IReadOnlyList<int> picked = dice.PickDistinct(source, 2);

        Assert.Equal(2, picked.Count);
        Assert.Equal(2, picked.Distinct().Count());
    }

    [Fact]
    public void PickDistinctClampsToSourceSizeRatherThanThrowing()
    {
        DiceRoller dice = Roller();
        int[] source = [1, 2, 3];

        Assert.Equal(3, dice.PickDistinct(source, 10).Count);
    }

    [Fact]
    public void PickDistinctOfZeroReturnsEmpty() =>
        Assert.Empty(Roller().PickDistinct<int>([1, 2, 3], 0));

    [Fact]
    public void PickWeightedRespectsWeights()
    {
        DiceRoller dice = Roller(5);
        (string Name, int Weight)[] options = [("common", 90), ("rare", 10)];
        var counts = new Dictionary<string, int> { ["common"] = 0, ["rare"] = 0 };

        for (int i = 0; i < 10_000; i++)
        {
            counts[dice.PickWeighted(options, o => o.Weight).Name]++;
        }

        Assert.True(counts["common"] > counts["rare"] * 5);
        Assert.True(counts["rare"] > 0, "A weighted option should still be reachable.");
    }

    [Fact]
    public void PickWeightedIgnoresZeroWeightOptions()
    {
        DiceRoller dice = Roller();
        (string Name, int Weight)[] options = [("never", 0), ("always", 1)];

        for (int i = 0; i < 200; i++)
        {
            Assert.Equal("always", dice.PickWeighted(options, o => o.Weight).Name);
        }
    }

    [Fact]
    public void PickWeightedFallsBackToUniformWhenAllWeightsAreZero()
    {
        DiceRoller dice = Roller();
        (string Name, int Weight)[] options = [("a", 0), ("b", 0)];

        Assert.Contains(dice.PickWeighted(options, o => o.Weight).Name, new[] { "a", "b" });
    }

    [Fact]
    public void ShuffleKeepsEveryElement()
    {
        DiceRoller dice = Roller();
        int[] source = [.. Enumerable.Range(0, 50)];

        IReadOnlyList<int> shuffled = dice.Shuffle(source);

        Assert.Equal(source.Order(), shuffled.Order());
    }

    [Fact]
    public void PickFromEmptyCollectionThrows() =>
        Assert.Throws<ArgumentException>(() => Roller().Pick<int>([]));

    [Fact]
    public void ChanceIsApproximatelyCalibrated()
    {
        DiceRoller dice = Roller(3);
        int hits = Enumerable.Range(0, 20_000).Count(_ => dice.Chance(25));

        Assert.InRange(hits / 20_000.0, 0.22, 0.28);
    }

    [Fact]
    public void ChanceHandlesCertainAndImpossible()
    {
        DiceRoller dice = Roller();

        Assert.False(dice.Chance(0));
        Assert.True(dice.Chance(100));
    }
}
