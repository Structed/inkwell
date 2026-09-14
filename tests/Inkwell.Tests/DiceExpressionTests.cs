using Structed.Inkwell.Generation;
using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Tests;

public class DiceExpressionTests
{
    private static DiceRoller Roller(uint seed = 1) => new(new Pcg32(seed));

    [Theory]
    // A bare count and sides, with and without an explicit multiplier.
    [InlineData("d2", 1, 2)]
    [InlineData("d6", 1, 6)]
    [InlineData("2d6", 2, 12)]
    [InlineData("d6 x 10", 10, 60)]
    // A trailing currency suffix is ignored, whatever a game happens to call its money.
    [InlineData("d6p", 1, 6)]
    [InlineData("d6 x 10p", 10, 60)]
    [InlineData("d4 x 1000p", 1000, 4000)]
    [InlineData("4d6 coins", 4, 24)]
    [InlineData("d6 x 10 gp", 10, 60)]
    public void RollsStayWithinTheExpressionsRange(string expression, int min, int max)
    {
        DiceRoller dice = Roller();

        for (int i = 0; i < 500; i++)
        {
            int? value = DiceExpression.TryRoll(dice, expression);

            Assert.NotNull(value);
            Assert.InRange(value!.Value, min, max);
        }
    }

    [Fact]
    public void MultipliedExpressionsLandOnMultiples()
    {
        DiceRoller dice = Roller();

        for (int i = 0; i < 200; i++)
        {
            Assert.Equal(0, DiceExpression.TryRoll(dice, "d6 x 10p")!.Value % 10);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("free")]
    [InlineData("10%")]
    [InlineData("x10p")]
    [InlineData("d0")]
    // A word is not a die roll just because it contains a "d". Worth pinning, because the currency
    // suffix is matched loosely and it would be easy to widen it until prose started parsing.
    [InlineData("not dice")]
    [InlineData("a dozen")]
    public void UnparsableExpressionsReturnNull(string? expression) =>
        Assert.Null(DiceExpression.TryRoll(Roller(), expression));

    [Fact]
    public void RollFallsBackWhenTheExpressionIsUnusable() =>
        Assert.Equal(7, DiceExpression.Roll(Roller(), "not dice", fallback: 7));
}
