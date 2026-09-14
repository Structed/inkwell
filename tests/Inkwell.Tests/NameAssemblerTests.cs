using Structed.Inkwell.Generation;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers assembling a name from parts without leaving the seam visible.
/// </summary>
public class NameAssemblerTests
{
    [Theory]
    // A doubled letter at the seam is collapsed: Oaks + stand would read "Oaksstand".
    [InlineData("Oaks", "stand", "Oakstand")]
    [InlineData("Moon", "nest", "Moonest")]
    [InlineData("Stump", "pond", "Stumpond")]
    [InlineData("Fig", "grove", "Figrove")]
    // A trailing 'e' before a vowel is dropped: Rose + ashe would read "Roseashe".
    [InlineData("Rose", "ashe", "Rosashe")]
    [InlineData("Stone", "ashe", "Stonashe")]
    // Clean joins are left untouched.
    [InlineData("Willow", "ville", "Willowville")]
    [InlineData("Black", "creek", "Blackcreek")]
    [InlineData("Berry", "mill", "Berrymill")]
    public void JoinSmoothsTheSeam(string start, string end, string expected) =>
        Assert.Equal(expected, NameAssembler.Join(start, end));

    [Fact]
    public void JoinAlwaysCapitalises() => Assert.Equal("Oakstand", NameAssembler.Join("oaks", "stand"));

    [Theory]
    [InlineData("", "thorpe", "Thorpe")]
    [InlineData("Oaks", "", "Oaks")]
    [InlineData("", "", "")]
    public void JoinHandlesMissingHalves(string start, string end, string expected) =>
        Assert.Equal(expected, NameAssembler.Join(start, end));

    [Fact]
    public void JoinNeverLeavesATripledLetter() =>
        Assert.Equal("Fellland".Replace("lll", "ll"), NameAssembler.Join("Fell", "lland"));

    [Fact]
    public void CapitaliseLeavesTheRestOfTheWordAlone() =>
        Assert.Equal("McTavish", NameAssembler.Capitalise("mcTavish"));

    [Fact]
    public void FillsASignTemplate() =>
        Assert.Equal(
            "The Crooked Beetle",
            NameAssembler.FromTemplate(
                "The {adjective} {noun}", ("adjective", "Crooked"), ("noun", "Beetle")));

    [Fact]
    public void ClosesTheGapLeftByASlotTheLanguageDoesNotUse() =>
        Assert.Equal(
            "The Crooked Beetle",
            NameAssembler.FromTemplate(
                "{article} The {adjective} {noun}",
                ("article", ""),
                ("adjective", "Crooked"),
                ("noun", "Beetle")));

    [Fact]
    public void UsesTheSlotTheLanguageDoesNeed() =>
        Assert.Equal(
            "Zum krummen Käfer",
            NameAssembler.FromTemplate(
                "{article} {adjective} {noun}",
                ("article", "Zum"),
                ("adjective", "krummen"),
                ("noun", "Käfer")));

    /// <summary>
    /// The one that bites: filling <c>{noun}</c> first would turn <c>{noun2}</c> into "Beetle2".
    /// </summary>
    [Fact]
    public void ANumberedSlotSurvivesItsUnnumberedNeighbour() =>
        Assert.Equal(
            "Beetle and Thorn",
            NameAssembler.FromTemplate("{noun} and {noun2}", ("noun", "Beetle"), ("noun2", "Thorn")));

    [Fact]
    public void TheOrderValuesAreListedInDoesNotMatter() =>
        Assert.Equal(
            NameAssembler.FromTemplate("{noun} and {noun2}", ("noun", "Beetle"), ("noun2", "Thorn")),
            NameAssembler.FromTemplate("{noun} and {noun2}", ("noun2", "Thorn"), ("noun", "Beetle")));
}
