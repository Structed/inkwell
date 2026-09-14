using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>
/// Mints the identifiers a Fantasia Archive document needs.
/// </summary>
/// <remarks>
/// Derived rather than drawn at random, so exporting the same thing twice produces the same
/// documents. That makes a second merge a no-op instead of a duplicate set, and it keeps an
/// exporter a pure function like the generator feeding it. <c>Guid.NewGuid</c> would give neither.
/// </remarks>
public static class DocumentIdentity
{
    /// <summary>
    /// The id and revision for one document, keyed by a stable path.
    /// </summary>
    /// <param name="seed">The root seed of whatever is being exported.</param>
    /// <param name="path">
    /// A path that must identify the document <em>and</em> the exact thing it belongs to. Two
    /// places that share a seed but differ in their settings or locks are different places, and
    /// giving their documents the same identity would make the second one un-importable: the app
    /// loads with <c>new_edits: false</c>, so a document whose id and revision are already present
    /// is quietly discarded rather than applied.
    /// </param>
    public static (string Id, string Revision) For(uint seed, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Pcg32 random = SeedDerivation.CreateStream(seed, path);

        return (FormatUuid(NextBytes(random)), "1-" + FormatHex(NextBytes(random)));
    }

    private static byte[] NextBytes(Pcg32 random)
    {
        byte[] bytes = new byte[16];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            uint value = random.NextUInt32();
            bytes[i] = (byte)value;
            bytes[i + 1] = (byte)(value >> 8);
            bytes[i + 2] = (byte)(value >> 16);
            bytes[i + 3] = (byte)(value >> 24);
        }

        return bytes;
    }

    /// <summary>
    /// Shapes 16 bytes into a version 4 UUID.
    /// </summary>
    /// <remarks>
    /// The app mints its own ids as version 4, and its repair tooling recognises documents by that
    /// shape, so an id that merely looks random is not enough: the version and variant bits have to
    /// be right. The randomness behind them is this project's, and reproducible.
    /// </remarks>
    private static string FormatUuid(byte[] bytes)
    {
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        string hex = FormatHex(bytes);
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    private static string FormatHex(byte[] bytes) => Convert.ToHexStringLower(bytes);
}
