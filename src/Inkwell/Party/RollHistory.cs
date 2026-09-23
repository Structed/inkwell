using System.Text.Json;

namespace Structed.Inkwell.Party;

/// <summary>
/// A batch of rolls, for catching somebody up when they arrive late.
/// </summary>
/// <remarks>
/// <para>
/// Carried as an array of message <em>strings</em> rather than an array of objects, so that every
/// roll in a replayed history goes through exactly the same reader — and the same refusals — as a
/// roll that arrived on its own. A second, laxer path into the log is precisely the sort of thing
/// that is safe on the day it is written and a hole a year later.
/// </para>
/// <para>
/// Bounded twice over: the whole batch has a size, and the number of rolls in it has a limit. One
/// peer answering a newcomer's question must not be able to hand them an afternoon's work.
/// </para>
/// </remarks>
public static class RollHistory
{
    /// <summary>The most rolls one batch may carry.</summary>
    public const int MaximumCount = 60;

    /// <summary>The largest batch that will be read.</summary>
    public const int MaximumBytes = 64 * 1024;

    /// <summary>Packs a history for sending.</summary>
    public static string Write(IEnumerable<RollMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        string[] packed = [.. messages.Take(MaximumCount).Select(message => message.Write())];

        return JsonSerializer.Serialize(packed, PartyJsonContext.Default.StringArray);
    }

    /// <summary>Unpacks a history, dropping anything in it that does not read cleanly.</summary>
    /// <remarks>
    /// One bad roll loses that roll, not the batch. A peer running a slightly different version is
    /// a likelier explanation for an unreadable entry than an attack, and throwing the whole history
    /// away would make the table quietly useless rather than visibly wrong.
    /// </remarks>
    public static IReadOnlyList<RollMessage> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumBytes)
        {
            return [];
        }

        string[]? packed;

        try
        {
            packed = JsonSerializer.Deserialize(json, PartyJsonContext.Default.StringArray);
        }
        catch (JsonException)
        {
            return [];
        }

        if (packed is null)
        {
            return [];
        }

        List<RollMessage> messages = [];

        foreach (string entry in packed.Take(MaximumCount))
        {
            if (RollMessage.TryRead(entry, out RollMessage message))
            {
                messages.Add(message);
            }
        }

        return messages;
    }
}
