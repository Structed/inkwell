namespace Structed.Inkwell.Dice;

/// <summary>
/// What a handful of dice shows when read as a poker hand.
/// </summary>
/// <remarks>
/// <para>
/// Multiplicities are counted <em>exactly</em>: three matching dice are a triplet and not also a
/// pair. That is not a stylistic choice. A full house is defined as a pair together with a triplet,
/// and if a triplet also counted as a pair then every triplet would be a full house — which is
/// plainly not what anybody means by the word.
/// </para>
/// <para>
/// Four or more matching dice are counted as a poker rather than exactly four, because a pool large
/// enough to show five of a kind has certainly shown four of a kind as well, and a reading that went
/// quiet at that point would deny a player the very result their luck had just earned.
/// </para>
/// </remarks>
public sealed record PokerResults
{
    /// <summary>Faces showing on exactly two dice.</summary>
    public required int Pairs { get; init; }

    /// <summary>Faces showing on exactly three dice.</summary>
    public required int Triplets { get; init; }

    /// <summary>Faces showing on four dice or more.</summary>
    public required int Pokers { get; init; }

    /// <summary>The longest run of consecutive faces present, counting each face once.</summary>
    /// <remarks>
    /// Reported as a length rather than a yes or no, because a sequence is only ever referred to by
    /// how long it is — a three-number sequence and a five-number sequence trigger different things.
    /// </remarks>
    public required int Sequence { get; init; }

    /// <summary>The most dice showing any one face.</summary>
    public required int Matched { get; init; }

    /// <summary>A pair and a triplet together.</summary>
    public bool FullHouse => Pairs >= 1 && Triplets >= 1;

    /// <summary>Nothing repeated and nothing in a row worth naming.</summary>
    public bool IsEmpty => Pairs == 0 && Triplets == 0 && Pokers == 0 && Sequence < DicePoker.ShortestSequence;
}

/// <summary>
/// Reads a pool of dice as a poker hand.
/// </summary>
/// <remarks>
/// <para>
/// Pairs, triplets and runs are arithmetic on a handful of numbers and belong to no particular game,
/// which is why this sits next to the notation parser rather than beside the rules that care about
/// it. What a pair <em>means</em> is somebody else's business entirely; this only says that there
/// is one.
/// </para>
/// <para>
/// Every die is read, including dice a keep rule dropped. The dice are lying on the table where the
/// whole party can see them, and a reading that quietly ignored some of them would disagree with
/// what everyone is looking at.
/// </para>
/// </remarks>
public static class DicePoker
{
    /// <summary>The shortest run anybody bothers to call a sequence.</summary>
    /// <remarks>
    /// Two consecutive numbers turn up in almost every roll of more than a couple of dice, so
    /// calling that a sequence would make the reading meaningless. Three is where it starts to be
    /// worth saying.
    /// </remarks>
    public const int ShortestSequence = 3;

    /// <summary>Reads the faces of <paramref name="outcome"/>.</summary>
    public static PokerResults Read(RollOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return Read(outcome.Faces);
    }

    /// <summary>Reads a bare list of faces.</summary>
    public static PokerResults Read(IEnumerable<int> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);

        Dictionary<int, int> counts = [];

        foreach (int face in faces)
        {
            counts[face] = counts.TryGetValue(face, out int seen) ? seen + 1 : 1;
        }

        int pairs = 0;
        int triplets = 0;
        int pokers = 0;
        int matched = 0;

        foreach (int count in counts.Values)
        {
            switch (count)
            {
                case 2:
                    pairs++;
                    break;
                case 3:
                    triplets++;
                    break;
                case >= 4:
                    pokers++;
                    break;
                default:
                    break;
            }

            matched = Math.Max(matched, count);
        }

        return new PokerResults
        {
            Pairs = pairs,
            Triplets = triplets,
            Pokers = pokers,
            Sequence = LongestRun(counts.Keys),
            Matched = matched
        };
    }

    /// <summary>The longest stretch of consecutive numbers among the faces present.</summary>
    private static int LongestRun(IEnumerable<int> distinctFaces)
    {
        int[] ordered = [.. distinctFaces.Order()];

        if (ordered.Length == 0)
        {
            return 0;
        }

        int longest = 1;
        int run = 1;

        for (int index = 1; index < ordered.Length; index++)
        {
            run = ordered[index] == ordered[index - 1] + 1 ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        return longest;
    }
}
