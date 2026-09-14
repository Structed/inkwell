using Structed.Inkwell.Generation;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers how a locked value is written down.
/// </summary>
/// <remarks>
/// A lock means "this row of this table", not "these words", so that it survives a change of
/// language. Everything here exists to keep that distinction sharp, including the deliberate
/// refusal to throw on a stale reference: an export made against an older table should still open.
/// </remarks>
public class PinReferenceTests
{
    private static readonly string[] Table = ["alpha", "beta", "gamma"];

    [Fact]
    public void WritesAPositionalReference() => Assert.Equal("#12", PinReference.ForIndex(12));

    [Fact]
    public void WritesSeveralOnePerLine() => Assert.Equal("#1\n#4", PinReference.ForIndices([1, 4]));

    [Theory]
    [InlineData("#0", 0)]
    [InlineData("#12", 12)]
    public void ReadsAPositionBack(string pin, int expected)
    {
        Assert.True(PinReference.TryGetIndex(pin, out int index));
        Assert.Equal(expected, index);
        Assert.True(PinReference.IsReference(pin));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#-1")]
    [InlineData("#abc")]
    [InlineData("12")]
    [InlineData("Hollowbridge")]
    [InlineData("The #1 tavern")]
    public void TreatsAnythingElseAsLiteralText(string? pin)
    {
        Assert.False(PinReference.TryGetIndex(pin, out _));
        Assert.False(PinReference.IsReference(pin ?? ""));
    }

    [Fact]
    public void ResolvesAReferenceAgainstTheTable() => Assert.Equal("beta", PinReference.Resolve("#1", Table));

    [Fact]
    public void ResolvesLiteralTextToItself() =>
        Assert.Equal("Hollowbridge", PinReference.Resolve("Hollowbridge", Table));

    [Fact]
    public void FallsBackToThePinWhenTheTableNoLongerHasThatRow() =>
        Assert.Equal("#99", PinReference.Resolve("#99", Table));

    [Fact]
    public void FallsBackRatherThanThrowingOnAnEmptyTable() =>
        Assert.Equal("#0", PinReference.Resolve("#0", []));

    [Fact]
    public void ResolvesEveryLineOfAMultiValuePin() =>
        Assert.Equal(["alpha", "gamma"], PinReference.ResolveMany("#0\n#2", Table));

    [Fact]
    public void MixesReferencesAndTypedTextInOneSelection() =>
        Assert.Equal(["alpha", "Hollowbridge"], PinReference.ResolveMany("#0\nHollowbridge", Table));

    [Fact]
    public void IgnoresBlankLinesAndSurroundingSpace() =>
        Assert.Equal(["alpha", "beta"], PinReference.ResolveMany("#0\n\n  #1  \n", Table));

    [Fact]
    public void ARoundTripThroughAPinIsLossless()
    {
        string pin = PinReference.ForIndices([2, 0]);

        Assert.Equal(["gamma", "alpha"], PinReference.ResolveMany(pin, Table));
    }
}
