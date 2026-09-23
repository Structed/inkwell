using Structed.Inkwell.Dice;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Reading a handful of dice as a poker hand, which is what a good many games trigger on.
/// </summary>
public sealed class DicePokerTests
{
    [Theory]
    [InlineData(new[] { 3, 3 }, 1)]
    [InlineData(new[] { 3, 3, 5, 5 }, 2)]
    [InlineData(new[] { 3, 3, 5, 5, 6, 6 }, 3)]
    [InlineData(new[] { 3, 4, 5 }, 0)]
    public void PairsAreFacesOnExactlyTwoDice(int[] faces, int expected)
    {
        Assert.Equal(expected, DicePoker.Read(faces).Pairs);
    }

    /// <summary>
    /// A triplet is not also a pair, which is the whole reason a full house can mean anything.
    /// </summary>
    [Fact]
    public void ATripletIsNotAlsoAPair()
    {
        PokerResults poker = DicePoker.Read([4, 4, 4]);

        Assert.Equal(1, poker.Triplets);
        Assert.Equal(0, poker.Pairs);
        Assert.False(poker.FullHouse);
    }

    [Fact]
    public void AFullHouseIsAPairAndATriplet()
    {
        PokerResults poker = DicePoker.Read([4, 4, 4, 2, 2]);

        Assert.Equal(1, poker.Triplets);
        Assert.Equal(1, poker.Pairs);
        Assert.True(poker.FullHouse);
    }

    /// <summary>Four matching dice are a poker, and so are five.</summary>
    [Theory]
    [InlineData(new[] { 5, 5, 5, 5 }, 4)]
    [InlineData(new[] { 5, 5, 5, 5, 5 }, 5)]
    [InlineData(new[] { 5, 5, 5, 5, 5, 5 }, 6)]
    public void FourOrMoreMatchingDiceAreAPoker(int[] faces, int expectedMatched)
    {
        PokerResults poker = DicePoker.Read(faces);

        Assert.Equal(1, poker.Pokers);
        Assert.Equal(0, poker.Triplets);
        Assert.Equal(0, poker.Pairs);
        Assert.Equal(expectedMatched, poker.Matched);
    }

    /// <summary>
    /// A sequence is measured by how long it is, and a repeat does not extend it.
    /// </summary>
    [Theory]
    [InlineData(new[] { 1, 2, 3 }, 3)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 5)]
    [InlineData(new[] { 6, 4, 5, 1 }, 3)]
    [InlineData(new[] { 2, 2, 3, 3, 4 }, 3)]
    [InlineData(new[] { 1, 3, 5 }, 1)]
    [InlineData(new[] { 4, 4, 4 }, 1)]
    public void ASequenceIsTheLongestRunOfConsecutiveFaces(int[] faces, int expected)
    {
        Assert.Equal(expected, DicePoker.Read(faces).Sequence);
    }

    [Fact]
    public void NothingIsNothing()
    {
        Assert.True(DicePoker.Read([1, 3, 5]).IsEmpty);
        Assert.True(DicePoker.Read([]).IsEmpty);
        Assert.False(DicePoker.Read([1, 1]).IsEmpty);
        Assert.False(DicePoker.Read([1, 2, 3]).IsEmpty);
    }

    /// <summary>
    /// A hand is read by what it shows, not by the order the dice landed in.
    /// </summary>
    [Fact]
    public void TheOrderOfTheDiceDoesNotMatter()
    {
        PokerResults straight = DicePoker.Read([1, 1, 2, 2, 5]);
        PokerResults shuffled = DicePoker.Read([2, 5, 1, 2, 1]);

        Assert.Equal(straight, shuffled);
    }

    /// <summary>
    /// Dropped dice are read too, because they are lying on the table in front of everybody.
    /// </summary>
    [Fact]
    public void DiceAKeepRuleDroppedStillCount()
    {
        Assert.True(DiceNotation.TryParse("4d6kh3", out DiceNotation notation));

        RollOutcome outcome = new()
        {
            Notation = notation,
            Dice =
            [
                new RolledDie(6, true),
                new RolledDie(5, true),
                new RolledDie(4, true),
                new RolledDie(4, false)
            ],
            Total = 15,
            Seed = 0
        };

        PokerResults poker = DicePoker.Read(outcome);

        Assert.Equal(1, poker.Pairs);
        Assert.Equal(3, poker.Sequence);
    }
}
