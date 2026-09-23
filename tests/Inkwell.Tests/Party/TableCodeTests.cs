using Structed.Inkwell.Party;

namespace Structed.Inkwell.Tests;

/// <summary>
/// The table code is the whole of the access control, so it has to be unguessable and typable.
/// </summary>
public sealed class TableCodeTests
{
    [Fact]
    public void ACodeIsTwelveCharactersOfTheAlphabet()
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            string code = TableCode.Create();

            Assert.Equal(TableCode.Length, code.Length);
            Assert.All(code, character => Assert.Contains(character, TableCode.Alphabet));
        }
    }

    /// <summary>
    /// Nothing here is reproducible, which is the opposite of everything else in this tool.
    /// </summary>
    [Fact]
    public void TwoCodesAreNotTheSameCode()
    {
        HashSet<string> codes = [];

        for (int attempt = 0; attempt < 500; attempt++)
        {
            Assert.True(codes.Add(TableCode.Create()));
        }
    }

    /// <summary>The alphabet exists to stop a code being misread aloud.</summary>
    [Fact]
    public void TheAlphabetHasNoLookalikes()
    {
        Assert.Equal(32, TableCode.Alphabet.Length);
        Assert.Equal(32, TableCode.Alphabet.Distinct().Count());

        foreach (char confusing in "ilou")
        {
            Assert.DoesNotContain(confusing, TableCode.Alphabet);
        }
    }

    [Fact]
    public void ACodeReadsBackHoweverItWasWrittenDown()
    {
        string code = TableCode.Create();

        foreach (string written in new[]
        {
            code,
            code.ToUpperInvariant(),
            TableCode.Group(code),
            "  " + TableCode.Group(code).ToUpperInvariant() + "  ",
            TableCode.Group(code).Replace("-", " ", StringComparison.Ordinal)
        })
        {
            Assert.True(TableCode.TryParse(written, out string read), written);
            Assert.Equal(code, read);
        }
    }

    /// <summary>The slips people actually make when copying a code off a screen.</summary>
    [Theory]
    [InlineData("0123456789ab", "0123456789ab")]
    [InlineData("O123456789ab", "0123456789ab")]
    [InlineData("I123456789ab", "1123456789ab")]
    [InlineData("l123456789ab", "1123456789ab")]
    [InlineData("0123-4567-89ab", "0123456789ab")]
    public void TheUsualMisreadingsAreForgiven(string written, string expected)
    {
        Assert.True(TableCode.TryParse(written, out string read));
        Assert.Equal(expected, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0123456789a")]
    [InlineData("0123456789abc")]
    [InlineData("0123456789au")]
    [InlineData("0123456789a!")]
    [InlineData("0123456789a/")]
    [InlineData("../../etc/passwd")]
    public void AnythingElseIsRefused(string? written)
    {
        Assert.False(TableCode.TryParse(written, out _));
    }

    [Fact]
    public void GroupingIsPurelyDecorative()
    {
        Assert.Equal("0123-4567-89ab", TableCode.Group("0123456789ab"));
    }
}
