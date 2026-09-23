namespace Structed.Inkwell.Party;

/// <summary>
/// The single opinion about what text from another browser may look like.
/// </summary>
/// <remarks>
/// Two kinds of message now arrive at this table and both of them carry a name. A second cleaner,
/// written on a different day and agreeing in all the obvious cases, is exactly how a control
/// character ends up rendered beside one player's name and not another's. There is one, it is
/// boring, and everything that reads from the wire goes through it.
/// </remarks>
internal static class PartyText
{
    /// <summary>Trims, shortens, and drops anything that would not print.</summary>
    public static string Clean(string? value, int maximum)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        string trimmed = string.Concat(value.Where(character => !char.IsControl(character))).Trim();

        return trimmed.Length <= maximum ? trimmed : trimmed[..maximum].Trim();
    }
}
