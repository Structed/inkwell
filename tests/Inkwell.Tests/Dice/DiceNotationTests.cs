using Structed.Inkwell.Dice;

namespace Structed.Inkwell.Tests;

/// <summary>
/// What a person may type into the box, and what must be refused.
/// </summary>
/// <remarks>
/// The refusals matter more than the acceptances. Whatever this parses is rolled, printed, and sent
/// to everyone else at the table, so anything it lets through unbounded is something other people's
/// browsers have to draw.
/// </remarks>
public sealed class DiceNotationTests
{
    [Theory]
    [InlineData("d20", "1d20")]
    [InlineData("D20", "1d20")]
    [InlineData("2d6", "2d6")]
    [InlineData(" 2 d 6 ", "2d6")]
    [InlineData("2d6+1", "2d6+1")]
    [InlineData("2d6-3", "2d6-3")]
    [InlineData("4d6kh3", "4d6kh3")]
    [InlineData("4d6KL1", "4d6kl1")]
    [InlineData("4d6kh3+2", "4d6kh3+2")]
    [InlineData("2d20kh", "2d20kh1")]
    public void AWellFormedRollReadsBackCanonically(string typed, string expected)
    {
        Assert.True(DiceNotation.TryParse(typed, out DiceNotation notation));
        Assert.Equal(expected, notation.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("d")]
    [InlineData("2d")]
    [InlineData("d0")]
    [InlineData("d1")]
    [InlineData("0d6")]
    [InlineData("101d6")]
    [InlineData("1d1001")]
    [InlineData("1d6+1001")]
    [InlineData("99999d6")]
    [InlineData("1d6k")]
    [InlineData("1d6kh0")]
    [InlineData("1d6kx2")]
    [InlineData("1d6+")]
    [InlineData("1d6++1")]
    [InlineData("2d6 and then some")]
    [InlineData("roll d6 please")]
    [InlineData("1d6;alert(1)")]
    public void NonsenseIsRefusedRatherThanGuessedAt(string? typed)
    {
        Assert.False(DiceNotation.TryParse(typed, out _));
    }

    /// <summary>
    /// A roll must be the whole of what was typed, not something found inside it.
    /// </summary>
    /// <remarks>
    /// This text is echoed into other people's logs. An unanchored pattern would find <c>d6</c> in a
    /// sentence and roll something nobody asked for, under somebody else's name.
    /// </remarks>
    [Fact]
    public void ARollIsNotFoundInsideASentence()
    {
        Assert.False(DiceNotation.TryParse("I would like to roll d6 now", out _));
    }

    [Fact]
    public void AVeryLongStringIsNotEvenLookedAt()
    {
        Assert.False(DiceNotation.TryParse(new string('1', 200) + "d6", out _));
    }

    /// <summary>Keeping every die is keeping all of them, however it was spelled.</summary>
    [Fact]
    public void KeepingAsManyDiceAsWereRolledIsNoKeepRuleAtAll()
    {
        Assert.True(DiceNotation.TryParse("3d6kh3", out DiceNotation kept));
        Assert.True(DiceNotation.TryParse("3d6", out DiceNotation plain));

        Assert.Equal(KeepRule.All, kept.Keep);
        Assert.Equal(plain, kept);
        Assert.Equal("3d6", kept.Text);
    }

    [Fact]
    public void ACanonicalRollParsesBackToItself()
    {
        foreach (string typed in new[] { "d20", "2d6+1", "4d6kh3", "10d10-2", "5d8kl2+3" })
        {
            Assert.True(DiceNotation.TryParse(typed, out DiceNotation first));
            Assert.True(DiceNotation.TryParse(first.Text, out DiceNotation second));
            Assert.Equal(first, second);
        }
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(101, 6)]
    [InlineData(2, 1)]
    [InlineData(2, 1001)]
    public void APoolOutsideTheBoundsIsNotBuilt(int count, int sides)
    {
        Assert.Null(DiceNotation.Pool(count, sides));
    }
}
