using System.Numerics;

namespace Structed.Inkwell.Randomness;

/// <summary>
/// PCG-XSH-RR 64/32, the "pcg32" variant of Melissa O'Neill's permuted congruential generator.
/// </summary>
/// <remarks>
/// Chosen because the algorithm is fixed and externally specified, so a given seed produces the
/// same stream forever regardless of runtime version. See https://www.pcg-random.org/.
/// </remarks>
public sealed class Pcg32 : IRandomSource
{
    public const ulong DefaultSequence = 0xDA3E39CB94B95BDBUL;

    private const ulong Multiplier = 6364136223846793005UL;

    private readonly ulong _increment;
    private ulong _state;

    public Pcg32(ulong state, ulong sequence = DefaultSequence)
    {
        // The increment must be odd for the LCG to reach full period.
        _increment = (sequence << 1) | 1UL;
        _state = 0UL;
        NextUInt32();
        _state = unchecked(_state + state);
        NextUInt32();
    }

    public uint NextUInt32()
    {
        ulong oldState = _state;
        _state = unchecked(oldState * Multiplier + _increment);

        uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rotation = (int)(oldState >> 59);
        return BitOperations.RotateRight(xorShifted, rotation);
    }

    public uint NextUInt32(uint exclusiveBound)
    {
        ArgumentOutOfRangeException.ThrowIfZero(exclusiveBound);

        // Rejection sampling: discard the tail that would otherwise bias the modulo.
        uint threshold = (uint)((0x1_0000_0000UL - exclusiveBound) % exclusiveBound);
        while (true)
        {
            uint value = NextUInt32();
            if (value >= threshold)
            {
                return value % exclusiveBound;
            }
        }
    }
}
