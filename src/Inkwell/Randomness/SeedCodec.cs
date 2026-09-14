using System.Text;

namespace Structed.Inkwell.Randomness;

/// <summary>
/// Encodes a root seed as a short, URL-friendly base-36 string.
/// </summary>
/// <remarks>
/// A <see cref="uint"/> seed is at most seven base-36 characters, which keeps shared links short.
/// Decoding is deliberately lenient about case and surrounding whitespace so a seed pasted from
/// chat or a session log still works.
/// </remarks>
public static class SeedCodec
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz";

    public static string Encode(uint seed)
    {
        if (seed == 0)
        {
            return "0";
        }

        Span<char> buffer = stackalloc char[7];
        int index = buffer.Length;
        uint value = seed;

        while (value > 0)
        {
            buffer[--index] = Alphabet[(int)(value % 36)];
            value /= 36;
        }

        return new string(buffer[index..]);
    }

    public static bool TryDecode(string? text, out uint seed)
    {
        seed = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> trimmed = text.AsSpan().Trim();
        ulong accumulator = 0;

        foreach (char raw in trimmed)
        {
            int digit = Alphabet.IndexOf(char.ToLowerInvariant(raw));
            if (digit < 0)
            {
                return false;
            }

            accumulator = accumulator * 36 + (ulong)digit;
            if (accumulator > uint.MaxValue)
            {
                return false;
            }
        }

        seed = (uint)accumulator;
        return true;
    }

    /// <summary>
    /// Parses a seed, falling back to a fresh random one when the text is missing or malformed.
    /// </summary>
    public static uint DecodeOrRandom(string? text) =>
        TryDecode(text, out uint seed) ? seed : CreateRandom();

    /// <summary>Creates a non-reproducible seed for something brand new.</summary>
    public static uint CreateRandom() =>
        (uint)Random.Shared.NextInt64(0, uint.MaxValue + 1L);
}
