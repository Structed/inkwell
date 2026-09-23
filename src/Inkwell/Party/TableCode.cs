using System.Security.Cryptography;
using System.Text;

namespace Structed.Inkwell.Party;

/// <summary>
/// The code that decides which players share a table.
/// </summary>
/// <remarks>
/// <para>
/// There is no server here, so this code is the whole of the access control: anyone who can guess it
/// can listen to the table's rolls. That makes its length a security parameter rather than a matter
/// of taste. Twelve characters of a thirty-two letter alphabet is sixty bits, which is not guessable
/// by anyone, and still short enough to read down a phone line if the link will not paste.
/// </para>
/// <para>
/// The alphabet is Crockford's: no <c>i</c>, <c>l</c>, <c>o</c> or <c>u</c>, so nothing can be
/// misread as a digit and nothing accidentally spells anything. Reading is forgiving — case,
/// grouping dashes and the classic <c>l</c>-for-<c>1</c> slip are all absorbed — because a code that
/// is typed by hand at all is a code that is typed by hand wrongly.
/// </para>
/// </remarks>
public static class TableCode
{
    /// <summary>Crockford's base-32 alphabet, lowercase.</summary>
    public const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";

    /// <summary>How many characters a code has.</summary>
    public const int Length = 12;

    private const int GroupSize = 4;

    /// <summary>Invents a code no one has used.</summary>
    /// <remarks>
    /// Drawn from the cryptographic generator, not the seeded one. Everything else in the engine is
    /// reproducible on purpose; a table code must be the opposite of reproducible. Thirty-two
    /// divides evenly into a byte's 256 values, so masking off five bits is unbiased and needs no
    /// rejection loop.
    /// </remarks>
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[Length];
        RandomNumberGenerator.Fill(bytes);

        StringBuilder code = new(Length);

        foreach (byte value in bytes)
        {
            code.Append(Alphabet[value & 0x1F]);
        }

        return code.ToString();
    }

    /// <summary>Reads a code however it was written down.</summary>
    public static bool TryParse(string? text, out string code)
    {
        code = "";

        if (string.IsNullOrWhiteSpace(text) || text.Length > Length * 4)
        {
            return false;
        }

        StringBuilder read = new(Length);

        foreach (char raw in text)
        {
            char character = char.ToLowerInvariant(raw);

            // Grouping is decoration, and people paste codes with whatever surrounds them.
            if (character is '-' or ' ' or '\t' or '_')
            {
                continue;
            }

            character = character switch
            {
                'i' or 'l' => '1',
                'o' => '0',
                _ => character
            };

            if (!Alphabet.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }

            if (read.Length == Length)
            {
                return false;
            }

            read.Append(character);
        }

        if (read.Length != Length)
        {
            return false;
        }

        code = read.ToString();
        return true;
    }

    /// <summary>Breaks a code into groups of four, for reading aloud.</summary>
    public static string Group(string code)
    {
        ArgumentNullException.ThrowIfNull(code);

        StringBuilder grouped = new(code.Length + (code.Length / GroupSize));

        for (int index = 0; index < code.Length; index++)
        {
            if (index > 0 && index % GroupSize == 0)
            {
                grouped.Append('-');
            }

            grouped.Append(code[index]);
        }

        return grouped.ToString();
    }
}
