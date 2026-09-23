using System.Text.Json;

namespace Structed.Inkwell.Party;

/// <summary>
/// What a player says on arriving: their name, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// A table where only the people who have rolled are visible is a table that looks empty between
/// fights, so each browser says what it is called when somebody new turns up. That is the whole of
/// it. There is no status, no avatar, no capability list and no room for one: a message that can
/// only carry a name cannot be talked into carrying anything more interesting, which matters
/// because these arrive from whoever has the table code.
/// </para>
/// <para>
/// The hail deliberately does not say who sent it. Identity comes from the transport, which knows
/// which connection a message actually came in on, so a peer cannot hail on somebody else's behalf
/// or rename a player sitting across the table.
/// </para>
/// </remarks>
public sealed record Hail
{
    /// <summary>The only version this code speaks.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The longest JSON this reader will consider.</summary>
    /// <remarks>
    /// A name and a timestamp come to well under a hundred bytes. Half a kilobyte is room to be
    /// wrong in, and small enough that a peer shouting hellos is shouting cheap ones.
    /// </remarks>
    public const int MaximumBytes = 512;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>What they are called, as they chose to be called.</summary>
    public string Player { get => field ?? ""; init; } = "";

    /// <summary>When it was said, by the sender's clock.</summary>
    /// <remarks>
    /// Not used for ordering — arrival decides that — and kept only so a hail can be told apart
    /// from the one before it when reading a log of what happened.
    /// </remarks>
    public long At { get; init; }

    /// <summary>Writes a hail for this player.</summary>
    public static Hail From(string? player, DateTimeOffset at) => new()
    {
        Player = PartyText.Clean(player, RollMessage.MaximumNameLength),
        At = at.ToUnixTimeMilliseconds()
    };

    /// <summary>Writes the hail for the wire.</summary>
    public string Write() => JsonSerializer.Serialize(this, PartyJsonContext.Default.Hail);

    /// <summary>
    /// Reads a hail from another player, refusing anything that is not exactly that.
    /// </summary>
    /// <remarks>
    /// A nameless hail is refused rather than shown as a blank seat: the point of the message is
    /// the name, and an empty one says nothing the peer count did not already say.
    /// </remarks>
    public static bool TryRead(string? json, out Hail hail)
    {
        hail = null!;

        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBytes)
        {
            return false;
        }

        Hail? read;

        try
        {
            read = JsonSerializer.Deserialize(json, PartyJsonContext.Default.Hail);
        }
        catch (JsonException)
        {
            return false;
        }

        if (read is null || read.Version != CurrentVersion)
        {
            return false;
        }

        string player = PartyText.Clean(read.Player, RollMessage.MaximumNameLength);

        if (player.Length == 0)
        {
            return false;
        }

        hail = new Hail { Player = player, At = read.At };
        return true;
    }
}
