using Structed.Inkwell.Party;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Who is at the table, kept straight when people arrive, rename themselves and wander off.
/// </summary>
/// <remarks>
/// Every one of these cases is somebody else's browser talking. The roster is the one place the
/// page trusts to say how many chairs to draw, so it has to stay bounded, stay in order, and never
/// let one player's message land on another player's seat.
/// </remarks>
public sealed class RosterTests
{
    private static Hail Named(string name) => Hail.From(name, DateTimeOffset.UnixEpoch);

    [Fact]
    public void APlayerWhoHailsIsSeated()
    {
        Roster roster = new();

        Assert.True(roster.Greet("peer-1", Named("Ada")));

        Assert.Equal(1, roster.Count);
        Assert.Equal("Ada", roster.Seats[0].Name);
    }

    [Fact]
    public void PlayersAreListedInTheOrderTheyArrived()
    {
        Roster roster = new();

        roster.Greet("peer-1", Named("Ada"));
        roster.Greet("peer-2", Named("Bran"));
        roster.Greet("peer-3", Named("Cass"));

        Assert.Equal(["Ada", "Bran", "Cass"], roster.Seats.Select(seat => seat.Name));
    }

    /// <summary>
    /// A rename is the same seat with different wording on it.
    /// </summary>
    /// <remarks>
    /// People fix their name after joining, usually while somebody is mid-sentence about it. Moving
    /// them to the end of the list for it would make the table jump about for everybody else.
    /// </remarks>
    [Fact]
    public void RenamingKeepsThePlaceInTheList()
    {
        Roster roster = new();

        roster.Greet("peer-1", Named("Ada"));
        roster.Greet("peer-2", Named("Bran"));

        Assert.True(roster.Greet("peer-1", Named("Ada the Bold")));

        Assert.Equal(2, roster.Count);
        Assert.Equal(["Ada the Bold", "Bran"], roster.Seats.Select(seat => seat.Name));
    }

    [Fact]
    public void HailingTwiceWithTheSameNameChangesNothing()
    {
        Roster roster = new();

        Assert.True(roster.Greet("peer-1", Named("Ada")));
        Assert.False(roster.Greet("peer-1", Named("Ada")));

        Assert.Equal(1, roster.Count);
    }

    /// <summary>Two people are allowed to pick the same name, and often do.</summary>
    [Fact]
    public void TwoPlayersCalledTheSameThingAreTwoSeats()
    {
        Roster roster = new();

        roster.Greet("peer-1", Named("Bran"));
        roster.Greet("peer-2", Named("Bran"));

        Assert.Equal(2, roster.Count);
    }

    [Fact]
    public void LeavingClearsOnlyThatSeat()
    {
        Roster roster = new();

        roster.Greet("peer-1", Named("Ada"));
        roster.Greet("peer-2", Named("Bran"));

        Assert.True(roster.Leave("peer-1"));

        Assert.Equal(["Bran"], roster.Seats.Select(seat => seat.Name));
    }

    [Theory]
    [InlineData("peer-nobody")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LeavingASeatNobodyHasIsNotAChange(string? peer)
    {
        Roster roster = new();

        roster.Greet("peer-1", Named("Ada"));

        Assert.False(roster.Leave(peer));
        Assert.Equal(1, roster.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0000")]
    public void AHailFromNoConnectionAtAllIsIgnored(string? peer)
    {
        Roster roster = new();

        Assert.False(roster.Greet(peer, Named("Ada")));
        Assert.Equal(0, roster.Count);
    }

    [Fact]
    public void ANamelessHailIsIgnored()
    {
        Roster roster = new();

        Assert.False(roster.Greet("peer-1", new Hail { Player = "  " }));
        Assert.Equal(0, roster.Count);
    }

    /// <summary>Names are cleaned here too, not only by the reader that usually runs first.</summary>
    [Fact]
    public void ANameIsCleanedOnTheWayToASeat()
    {
        Roster roster = new();

        roster.Greet("peer-1", new Hail { Player = "  A\u0000da  " });

        Assert.Equal("Ada", roster.Seats[0].Name);
    }

    [Fact]
    public void AConnectionNameIsBoundedToo()
    {
        Roster roster = new();

        roster.Greet(new string('p', 500), Named("Ada"));

        Assert.Equal(Roster.MaximumPeerLength, roster.Seats[0].Peer.Length);
    }

    /// <summary>
    /// A peer that has found a way to be a thousand connections costs a full list, not the page.
    /// </summary>
    [Fact]
    public void TheTableStopsAtItsCapacity()
    {
        Roster roster = new();

        for (int index = 0; index < Roster.MaximumPlayers + 20; index++)
        {
            roster.Greet($"peer-{index}", Named($"Player {index}"));
        }

        Assert.Equal(Roster.MaximumPlayers, roster.Count);
    }

    /// <summary>A full table still lets the people at it rename themselves.</summary>
    [Fact]
    public void AFullTableStillAcceptsARename()
    {
        Roster roster = new();

        for (int index = 0; index < Roster.MaximumPlayers; index++)
        {
            roster.Greet($"peer-{index}", Named($"Player {index}"));
        }

        Assert.True(roster.Greet("peer-0", Named("Ada")));
        Assert.Equal("Ada", roster.Seats[0].Name);
        Assert.Equal(Roster.MaximumPlayers, roster.Count);
    }

    [Fact]
    public void LeavingTheTableForgetsEverybody()
    {
        Roster roster = new();

        roster.Greet("peer-1", Named("Ada"));

        Assert.True(roster.Clear());
        Assert.False(roster.Clear());
        Assert.Equal(0, roster.Count);
    }
}
