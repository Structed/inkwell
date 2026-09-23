using Microsoft.JSInterop;
using Structed.Inkwell.Dice;
using Structed.Inkwell.Party;
using Structed.Inkwell.Party.Blazor;

namespace Structed.Inkwell.Tests;

/// <summary>
/// The channel is where the decision about what leaves the machine is actually taken.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs against a fake runtime rather than a browser, which is the whole point of
/// keeping the rules on this side of the interop boundary: the question "can a private roll get
/// out" is answerable by reading the string that was handed to JavaScript, with no page, no relay
/// and no second machine involved.
/// </para>
/// <para>
/// The fake answers every call with <c>default</c> and remembers what it was asked. It is not
/// pretending to be a transport — a transport that worked would test the transport, and this is
/// testing the sentence spoken into it.
/// </para>
/// </remarks>
public sealed class PartyChannelTests
{
    private const string AppId = "structed-inkwell-tests";

    private static RollOutcome Rolled(string text = "4d6kh3+1", uint seed = 4242)
    {
        Assert.True(DiceNotation.TryParse(text, out DiceNotation notation));

        return DiceRolls.Roll(notation, seed);
    }

    private static async Task<(PartyChannel Channel, FakeRuntime Runtime)> JoinedAsync()
    {
        FakeRuntime runtime = new();
        PartyChannel channel = new(runtime, AppId) { Player = "Ada" };

        await channel.JoinAsync(TableCode.Create());

        Assert.True(channel.IsJoined);

        return (channel, runtime);
    }

    /// <summary>
    /// The module has to be asked for where the package actually puts it.
    /// </summary>
    /// <remarks>
    /// A static web asset from a Razor class library is served under
    /// <c>_content/[package id]</c>, so this path and the <c>PackageId</c> are one fact written in
    /// two places. Renaming the package without changing this leaves a table that fails to open on
    /// the deployed site and nowhere else.
    /// </remarks>
    [Fact]
    public async Task TheModuleIsImportedFromTheStaticWebAssetPath()
    {
        (_, FakeRuntime runtime) = await JoinedAsync();

        Assert.Equal(
            "./_content/Structed.Inkwell.Party.Blazor/js/party.js",
            Assert.Single(runtime.Imports));
    }

    /// <summary>
    /// The app id is the caller's to choose, and it has to reach the transport unaltered.
    /// </summary>
    /// <remarks>
    /// It namespaces the signalling, so two apps that disagree about it are two tables however
    /// carefully their players copy the code between them. Anything that quietly rewrote it would
    /// strand every table code already shared.
    /// </remarks>
    [Fact]
    public async Task TheAppIdIsHandedToTheTransportAsGiven()
    {
        (_, FakeRuntime runtime) = await JoinedAsync();

        object?[] join = runtime.Module.Calls.Single(call => call.Method == "join").Arguments;

        Assert.Equal(AppId, join[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AChannelWithoutAnAppIdIsRefused(string appId)
    {
        Assert.Throws<ArgumentException>(() => new PartyChannel(new FakeRuntime(), appId));
    }

    /// <summary>
    /// A private roll is ghosted before it is sent, not after.
    /// </summary>
    /// <remarks>
    /// Asserted against the string handed to JavaScript, because that is the thing that leaves. A
    /// test of the object the channel returned would pass just as happily while the dice went out
    /// over the relay.
    /// </remarks>
    [Fact]
    public async Task APrivateRollLeavesTheMachineAsAGhost()
    {
        (PartyChannel channel, FakeRuntime runtime) = await JoinedAsync();

        RollOutcome outcome = Rolled();
        await channel.RollAsync(outcome, new RollReading("strike/minor", 1), secret: true, "strike");

        string sent = Assert.IsType<string>(
            runtime.Module.Calls.Single(call => call.Method == "send").Arguments[0]);

        Assert.True(RollMessage.TryRead(sent, out RollMessage read));

        Assert.True(read.Secret);
        Assert.Equal("Ada", read.Player);
        Assert.Empty(read.Faces);
        Assert.Equal("", read.Notation);
        Assert.Equal(0, read.Total);
        Assert.Equal(0u, read.Seed);
        Assert.DoesNotContain("4d6", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("strike", sent, StringComparison.Ordinal);
    }

    /// <summary>The roller still sees their own dice; only the outgoing copy is emptied.</summary>
    [Fact]
    public async Task ThePrivateRollerKeepsTheirOwnDice()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        RollOutcome outcome = Rolled();
        RollMessage message = await channel.RollAsync(outcome, null, secret: true);

        RollEntry entry = Assert.Single(channel.Log.Entries);

        Assert.True(entry.IsMine);
        Assert.Equal(message.Id, entry.Message.Id);
        Assert.Equal(outcome.Faces, entry.Message.Faces);
        Assert.Equal(outcome.Total, entry.Message.Total);
        Assert.Equal(outcome.Notation.Text, entry.Message.Notation);
    }

    /// <summary>
    /// The second chance: a private roll is ghosted again on the way out to a newcomer.
    /// </summary>
    /// <remarks>
    /// The same roll is now in the log in full, because its owner has to be able to see it. This is
    /// the path that would hand it to a stranger, and it is deliberately not trusting the first
    /// ghosting to have happened.
    /// </remarks>
    [Fact]
    public async Task AReplayGhostsThePrivateRollAgain()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        RollOutcome outcome = Rolled();
        await channel.RollAsync(outcome, new RollReading("strike/minor", 1), secret: true, "strike");

        string told = channel.Replay();

        Assert.DoesNotContain("4d6", told, StringComparison.Ordinal);
        Assert.DoesNotContain("strike", told, StringComparison.Ordinal);

        RollMessage replayed = Assert.Single(RollHistory.Read(told));

        Assert.True(replayed.Secret);
        Assert.Empty(replayed.Faces);
        Assert.Equal(0, replayed.Total);
        Assert.Equal(0u, replayed.Seed);
    }

    /// <summary>An open roll is told in full, or the table would be pointless.</summary>
    [Fact]
    public async Task AnOpenRollTravelsWithItsDice()
    {
        (PartyChannel channel, FakeRuntime runtime) = await JoinedAsync();

        RollOutcome outcome = Rolled();
        await channel.RollAsync(outcome, null, secret: false);

        string sent = Assert.IsType<string>(
            runtime.Module.Calls.Single(call => call.Method == "send").Arguments[0]);

        Assert.True(RollMessage.TryRead(sent, out RollMessage read));

        Assert.False(read.Secret);
        Assert.Equal(outcome.Faces, read.Faces);
        Assert.Equal(outcome.Total, read.Total);
        Assert.Equal(outcome.Seed, read.Seed);
    }

    /// <summary>
    /// The transport's words for how it is going, and what they mean here.
    /// </summary>
    /// <remarks>
    /// Anything unrecognised is a failure rather than a shrug. A status this code cannot read means
    /// the two halves disagree about something, and saying so is better than showing a table as
    /// open when nobody knows whether it is.
    /// </remarks>
    [Theory]
    [InlineData("joining", PartyStatus.Joining)]
    [InlineData("searching", PartyStatus.Searching)]
    [InlineData("open", PartyStatus.Open)]
    [InlineData("", PartyStatus.Failed)]
    [InlineData("Open", PartyStatus.Failed)]
    [InlineData("something else entirely", PartyStatus.Failed)]
    public async Task AStatusFromTheTransportIsReadOrTreatedAsFailure(string said, PartyStatus expected)
    {
        (PartyChannel channel, _) = await JoinedAsync();

        // Joining is where JoinAsync leaves it, so move off that first or the one case that maps
        // back to it would be indistinguishable from nothing having happened.
        channel.ReceiveStatus("open");
        channel.ReceiveStatus(said);

        Assert.Equal(expected, channel.Status);
    }

    /// <summary>A channel that is not at a table has no status to report.</summary>
    /// <remarks>
    /// The watcher in the transport keeps talking for a moment after leaving. Letting it reopen a
    /// table nobody is sitting at would be worse than ignoring it.
    /// </remarks>
    [Fact]
    public void AStatusArrivingWhileAwayIsIgnored()
    {
        PartyChannel channel = new(new FakeRuntime(), AppId);

        channel.ReceiveStatus("open");

        Assert.Equal(PartyStatus.Away, channel.Status);
    }

    /// <summary>Leaving forgets the table, and everything said at it.</summary>
    [Fact]
    public async Task LeavingClearsTheLogAndTheRoster()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        await channel.RollAsync(Rolled(), null, secret: false);
        channel.ReceiveHail("peer-1", Hail.From("Bran", DateTimeOffset.UnixEpoch).Write());

        Assert.NotEmpty(channel.Log.Entries);
        Assert.Equal(1, channel.Roster.Count);

        await channel.LeaveAsync();

        Assert.Empty(channel.Log.Entries);
        Assert.Equal(0, channel.Roster.Count);
        Assert.Equal("", channel.Code);
        Assert.Equal(PartyStatus.Away, channel.Status);
    }

    /// <summary>Answers every call with nothing, and remembers being asked.</summary>
    private sealed class FakeRuntime : IJSRuntime
    {
        public List<string> Imports { get; } = [];

        public FakeModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            if (identifier == "import")
            {
                Imports.Add(args?[0] as string ?? "");

                return ValueTask.FromResult((TValue)(object)Module);
            }

            return ValueTask.FromResult<TValue>(default!);
        }
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public List<(string Method, object?[] Arguments)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            Calls.Add((identifier, args ?? []));

            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
