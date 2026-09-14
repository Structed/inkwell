using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Tests;

public class SeedCodecTests
{
    [Theory]
    [InlineData(0u, "0")]
    [InlineData(1u, "1")]
    [InlineData(35u, "z")]
    [InlineData(36u, "10")]
    [InlineData(uint.MaxValue, "1z141z3")]
    public void EncodesToBase36(uint seed, string expected) =>
        Assert.Equal(expected, SeedCodec.Encode(seed));

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(12345u)]
    [InlineData(3_999_999_999u)]
    [InlineData(uint.MaxValue)]
    public void RoundTrips(uint seed)
    {
        Assert.True(SeedCodec.TryDecode(SeedCodec.Encode(seed), out uint decoded));
        Assert.Equal(seed, decoded);
    }

    [Fact]
    public void EncodedSeedsStayShort() =>
        Assert.True(SeedCodec.Encode(uint.MaxValue).Length <= 7);

    [Theory]
    [InlineData("  1z141z3  ")]
    [InlineData("1Z141Z3")]
    public void DecodingToleratesCaseAndWhitespace(string text)
    {
        Assert.True(SeedCodec.TryDecode(text, out uint decoded));
        Assert.Equal(uint.MaxValue, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a seed")]
    [InlineData("zzzzzzzz")]
    public void RejectsMalformedInput(string? text) =>
        Assert.False(SeedCodec.TryDecode(text, out _));

    [Fact]
    public void FallsBackToRandomSeedWhenInputIsUnusable()
    {
        // Should not throw, and should produce something usable rather than always zero.
        var seeds = new HashSet<uint>();
        for (int i = 0; i < 50; i++)
        {
            seeds.Add(SeedCodec.DecodeOrRandom("nonsense"));
        }

        Assert.True(seeds.Count > 1);
    }

    [Fact]
    public void ValidInputIsNotReplacedByRandomSeed() =>
        Assert.Equal(12345u, SeedCodec.DecodeOrRandom(SeedCodec.Encode(12345u)));
}
