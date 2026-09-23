namespace Structed.Inkwell.Party;

/// <summary>A player at the table, and the connection they are sitting on.</summary>
/// <param name="Peer">
/// The transport's name for their connection. Never shown: it is here so that two players called
/// Bran are two seats rather than one, and so that a rename lands on the right one.
/// </param>
/// <param name="Name">What to call them.</param>
public sealed record Seat(string Peer, string Name);

/// <summary>
/// Who is at the table, in the order they arrived.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by connection rather than by name, because names are chosen by the people using them and
/// two players are perfectly entitled to pick the same one. A rename is the same seat with
/// different wording on it, so the list does not reshuffle when somebody fixes a typo mid-session.
/// </para>
/// <para>
/// Bounded, like everything else that grows on the word of another browser. A dice table has a
/// dozen people at it on a wild night; the cap is far above that and exists only so a peer that has
/// found a way to appear as a thousand connections costs a capped list rather than the page.
/// </para>
/// <para>
/// Held in memory only, and emptied on leaving. Who was at a table is exactly as much nobody's
/// business afterwards as what they rolled.
/// </para>
/// </remarks>
public sealed class Roster
{
    /// <summary>How many players may be shown at once.</summary>
    public const int MaximumPlayers = 32;

    /// <summary>The longest connection name that will be accepted.</summary>
    public const int MaximumPeerLength = 64;

    private readonly List<Seat> seats = [];

    /// <summary>The table, in arrival order.</summary>
    public IReadOnlyList<Seat> Seats => seats;

    /// <summary>How many players are here, not counting whoever is reading.</summary>
    public int Count => seats.Count;

    /// <summary>Seats a player, or renames the one already sitting on that connection.</summary>
    /// <returns><c>true</c> if the table actually changed.</returns>
    public bool Greet(string? peer, Hail hail)
    {
        ArgumentNullException.ThrowIfNull(hail);

        string connection = PartyText.Clean(peer, MaximumPeerLength);
        string name = PartyText.Clean(hail.Player, RollMessage.MaximumNameLength);

        if (connection.Length == 0 || name.Length == 0)
        {
            return false;
        }

        int index = seats.FindIndex(seat => seat.Peer == connection);

        if (index >= 0)
        {
            if (seats[index].Name == name)
            {
                return false;
            }

            seats[index] = seats[index] with { Name = name };
            return true;
        }

        if (seats.Count >= MaximumPlayers)
        {
            return false;
        }

        seats.Add(new Seat(connection, name));
        return true;
    }

    /// <summary>Clears the seat a connection was using.</summary>
    /// <returns><c>true</c> if somebody was actually sitting there.</returns>
    public bool Leave(string? peer)
    {
        string connection = PartyText.Clean(peer, MaximumPeerLength);

        if (connection.Length == 0)
        {
            return false;
        }

        return seats.RemoveAll(seat => seat.Peer == connection) > 0;
    }

    /// <summary>Empties the table.</summary>
    /// <returns><c>true</c> if there was anybody to forget.</returns>
    public bool Clear()
    {
        if (seats.Count == 0)
        {
            return false;
        }

        seats.Clear();
        return true;
    }
}
