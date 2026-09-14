namespace Structed.Inkwell.Randomness;

/// <summary>
/// A source of deterministic pseudo-random numbers.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="System.Random"/>: its seeded output is not stable across .NET
/// versions (it changed between .NET Core 3.0 and .NET 6), which would silently invalidate every
/// previously shared seed URL on an SDK upgrade.
/// </remarks>
public interface IRandomSource
{
    /// <summary>Returns the next raw 32-bit value.</summary>
    uint NextUInt32();

    /// <summary>Returns a uniformly distributed value in <c>[0, exclusiveBound)</c>.</summary>
    uint NextUInt32(uint exclusiveBound);
}
