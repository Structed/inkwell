using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Tests;

public class Pcg32Tests
{
    /// <summary>
    /// The reference output of pcg32 for the canonical demo seeding, from O'Neill's
    /// <c>pcg32-demo.c</c> (<c>pcg32_srandom_r(&amp;rng, 42u, 54u)</c>).
    /// </summary>
    /// <remarks>
    /// This is the test that actually protects shared seed URLs. If it ever fails, the generator
    /// has drifted and every previously shared settlement link now resolves to different content.
    /// </remarks>
    [Fact]
    public void MatchesReferenceVectorsForCanonicalSeed()
    {
        var rng = new Pcg32(state: 42UL, sequence: 54UL);

        uint[] expected =
        [
            0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu
        ];

        uint[] actual = new uint[expected.Length];
        for (int i = 0; i < actual.Length; i++)
        {
            actual[i] = rng.NextUInt32();
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var first = new Pcg32(12345UL);
        var second = new Pcg32(12345UL);

        for (int i = 0; i < 64; i++)
        {
            Assert.Equal(first.NextUInt32(), second.NextUInt32());
        }
    }

    [Fact]
    public void DifferentSeedsDiverge()
    {
        var first = new Pcg32(1UL);
        var second = new Pcg32(2UL);

        uint[] a = [.. Enumerable.Range(0, 32).Select(_ => first.NextUInt32())];
        uint[] b = [.. Enumerable.Range(0, 32).Select(_ => second.NextUInt32())];

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(6u)]
    [InlineData(20u)]
    [InlineData(97u)]
    public void BoundedValuesStayInRange(uint bound)
    {
        var rng = new Pcg32(99UL);

        for (int i = 0; i < 5_000; i++)
        {
            Assert.InRange(rng.NextUInt32(bound), 0u, bound - 1);
        }
    }

    [Fact]
    public void BoundedValuesCoverTheWholeRange()
    {
        var rng = new Pcg32(7UL);
        var seen = new HashSet<uint>();

        for (int i = 0; i < 2_000; i++)
        {
            seen.Add(rng.NextUInt32(20));
        }

        Assert.Equal(20, seen.Count);
    }

    [Fact]
    public void ZeroBoundIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pcg32(1UL).NextUInt32(0));
}
