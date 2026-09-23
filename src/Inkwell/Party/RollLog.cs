namespace Structed.Inkwell.Party;

/// <summary>A roll in the table's history, and whether it was ours.</summary>
/// <param name="Message">The roll as it will be shown.</param>
/// <param name="IsMine">
/// <c>true</c> for a roll made on this machine. Only ever true locally: nothing sets it from the
/// wire, so a message cannot claim to be yours.
/// </param>
public sealed record RollEntry(RollMessage Message, bool IsMine);

/// <summary>
/// What the table has rolled, newest first.
/// </summary>
/// <remarks>
/// <para>
/// Held in memory only. The log is a shared conversation, not a document, and writing it to storage
/// would create a copy of other people's private-adjacent business that outlives the session it
/// belonged to.
/// </para>
/// <para>
/// Every path in and out is idempotent and bounded: rolls are keyed by id so a replayed history
/// merges rather than duplicates, and the whole thing is capped so a long session — or a peer
/// enthusiastically retransmitting — cannot grow the page without limit.
/// </para>
/// </remarks>
public sealed class RollLog
{
    /// <summary>How many rolls are kept before the oldest fall off.</summary>
    public const int Capacity = 200;

    /// <summary>How many rolls a newcomer is caught up with.</summary>
    /// <remarks>
    /// Shorter than <see cref="Capacity"/> deliberately. Somebody arriving mid-session wants the
    /// last few minutes, not the whole evening, and every replayed roll is a message every other
    /// peer may also be sending.
    /// </remarks>
    public const int ReplayCount = 40;

    private readonly List<RollEntry> entries = [];
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);

    /// <summary>The history, newest first.</summary>
    public IReadOnlyList<RollEntry> Entries => entries;

    /// <summary>Raised whenever the history changes.</summary>
    public event Action? Changed;

    /// <summary>Adds a roll, unless it is one already here.</summary>
    /// <returns><c>true</c> if the log actually changed.</returns>
    public bool Add(RollMessage message, bool isMine = false)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Id.Length == 0 || !seen.Add(message.Id))
        {
            return false;
        }

        entries.Insert(0, new RollEntry(message, isMine));

        while (entries.Count > Capacity)
        {
            RollEntry dropped = entries[^1];
            entries.RemoveAt(entries.Count - 1);
            seen.Remove(dropped.Message.Id);
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Merges a history sent by another player.</summary>
    /// <returns>How many of the rolls were new.</returns>
    public int Merge(IEnumerable<RollMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        // Oldest first, so that a batch arriving together ends up in the same order as it would have
        // done had it arrived one roll at a time.
        int added = 0;

        foreach (RollMessage message in messages.OrderBy(message => message.At))
        {
            if (message.Id.Length != 0 && !seen.Contains(message.Id))
            {
                added += Add(message) ? 1 : 0;
            }
        }

        return added;
    }

    /// <summary>
    /// The history as it may be told to somebody who has just joined.
    /// </summary>
    /// <remarks>
    /// Private rolls are ghosted on the way out, every time, with no regard for whose they were.
    /// The alternative — trusting that whatever went into the log was already safe — would make this
    /// method's correctness depend on every caller that ever adds to it, including ones not written
    /// yet. Ghosting an already-ghosted roll costs nothing and changes nothing.
    /// </remarks>
    public IReadOnlyList<RollMessage> Replay(int count = ReplayCount) =>
    [
        .. entries
            .Take(Math.Clamp(count, 0, Capacity))
            .Reverse()
            .Select(entry => entry.Message.Secret ? entry.Message.Ghost() : entry.Message)
    ];

    /// <summary>Empties the log.</summary>
    public void Clear()
    {
        if (entries.Count == 0)
        {
            return;
        }

        entries.Clear();
        seen.Clear();
        Changed?.Invoke();
    }
}
