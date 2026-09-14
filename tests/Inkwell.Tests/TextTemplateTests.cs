using Structed.Inkwell.Data;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers filling named slots in a localised format string.
/// </summary>
public class TextTemplateTests
{
    [Fact]
    public void FillsASlot() =>
        Assert.Equal("A village of 300 mice", TextTemplate.Format("A {size} of {count} mice", ("size", "village"), ("count", "300")));

    [Fact]
    public void SurvivesReorderingBecauseSlotsAreNamed() =>
        Assert.Equal("Ein Dorf mit 300 Mäusen", TextTemplate.Format("Ein {size} mit {count} Mäusen", ("size", "Dorf"), ("count", "300")));

    [Fact]
    public void LeavesAnUnmatchedSlotVisible() =>
        Assert.Equal("A village, {host}.", TextTemplate.Format("A {size}, {host}.", ("size", "village")));

    [Fact]
    public void TreatsANullValueAsEmpty() =>
        Assert.Equal("A village, .", TextTemplate.Format("A {size}, {host}.", ("size", "village"), ("host", null)));

    [Fact]
    public void ReplacesEveryOccurrence() =>
        Assert.Equal("6p for 6p", TextTemplate.Format("{a} for {a}", ("a", "6p")));

    [Theory]
    [InlineData("")]
    [InlineData("nothing to fill")]
    public void PassesThroughATemplateWithNoSlots(string template) =>
        Assert.Equal(template, TextTemplate.Format(template, ("size", "village")));

    [Fact]
    public void PassesThroughWhenNoValuesAreSupplied() =>
        Assert.Equal("A {size}", TextTemplate.Format("A {size}"));
}
