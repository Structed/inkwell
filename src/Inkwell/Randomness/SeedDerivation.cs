namespace Structed.Inkwell.Randomness;

/// <summary>
/// Derives independent, reproducible random streams from a single root seed plus a field path.
/// </summary>
/// <remarks>
/// <para>
/// This is what makes "lock the bits you like, re-roll the rest" work. If every value were drawn
/// from one shared stream, re-rolling the third shop would shift every value drawn after it. By
/// giving each field its own stream, keyed by a stable path such as <c>"shop/3/proprietor/quirk"</c>,
/// a field's value depends only on the root seed and its own identity.
/// </para>
/// <para>
/// <see cref="string.GetHashCode()"/> is unusable here: it is randomised per process, so it would
/// produce a different result on every page load. FNV-1a is used instead because it is fixed.
/// </para>
/// </remarks>
public static class SeedDerivation
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    /// <summary>FNV-1a over the UTF-16 code units of <paramref name="value"/>.</summary>
    public static ulong HashString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ulong hash = FnvOffsetBasis;
        foreach (char c in value)
        {
            hash = unchecked((hash ^ c) * FnvPrime);
        }

        return hash;
    }

    /// <summary>The SplitMix64 finalising mix, used to decorrelate nearby seed values.</summary>
    public static ulong Mix(ulong value)
    {
        unchecked
        {
            ulong z = value + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Derives the stream state for one field beneath <paramref name="rootSeed"/>.</summary>
    public static ulong Derive(uint rootSeed, string path) =>
        Mix(Mix(rootSeed) ^ HashString(path));

    /// <summary>Creates a generator for one field beneath <paramref name="rootSeed"/>.</summary>
    public static Pcg32 CreateStream(uint rootSeed, string path) =>
        new(Derive(rootSeed, path));
}
