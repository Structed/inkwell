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
    /// A host that names no relays gets exactly what it got before there was a way to name them.
    /// </summary>
    /// <remarks>
    /// Null is the transport's cue to fall back on Trystero's own draw. Anything else — an empty
    /// list included — would change which relays every existing table is signalled through.
    /// </remarks>
    [Fact]
    public async Task WithoutRelaysTheTransportIsLeftToChooseItsOwn()
    {
        (_, FakeRuntime runtime) = await JoinedAsync();

        object?[] join = runtime.Module.Calls.Single(call => call.Method == "join").Arguments;

        Assert.Null(join[1]);
    }

    /// <summary>
    /// The relays a host names reach the transport as named, in order, alongside the app id.
    /// </summary>
    /// <remarks>
    /// The draw Trystero makes on its own is fixed by the app id, so a relay that has died stays in
    /// it for good. Naming the relays is the only way round that, and it only works if what was
    /// named is what is used.
    /// </remarks>
    [Fact]
    public async Task NamedRelaysAreHandedToTheTransportAsGiven()
    {
        FakeRuntime runtime = new();
        PartyChannel channel = new(runtime, AppId, relays: ["wss://relay.example", "ws://localhost:7777/"]);

        await channel.JoinAsync(TableCode.Create());

        object?[] join = runtime.Module.Calls.Single(call => call.Method == "join").Arguments;

        Assert.Equal(AppId, join[0]);
        Assert.Equal(["wss://relay.example", "ws://localhost:7777/"], Assert.IsType<string[]>(join[1]));
    }

    /// <summary>A relay typed with stray spaces, or twice, is still one relay.</summary>
    [Fact]
    public async Task NamedRelaysAreTrimmedAndNotRepeated()
    {
        FakeRuntime runtime = new();
        PartyChannel channel = new(
            runtime,
            AppId,
            relays: [" wss://relay.example ", "wss://other.example", "wss://relay.example"]);

        await channel.JoinAsync(TableCode.Create());

        object?[] join = runtime.Module.Calls.Single(call => call.Method == "join").Arguments;

        Assert.Equal(["wss://relay.example", "wss://other.example"], Assert.IsType<string[]>(join[1]));
    }

    /// <summary>
    /// A relay the transport could never open is refused while it is still the host's mistake.
    /// </summary>
    /// <remarks>
    /// Left to the browser, each of these fails the way a deleted relay does: silently, costing the
    /// table a relay it believes it has.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relay.example")]
    [InlineData("https://relay.example")]
    [InlineData("wss://")]
    [InlineData("not a url")]
    public void ARelayThatIsNotAWebSocketUrlIsRefused(string relay)
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new PartyChannel(new FakeRuntime(), AppId, relays: ["wss://relay.example", relay]));

        Assert.Equal("relays", refused.ParamName);
    }

    /// <summary>An empty list is not "the defaults"; it is no way for anybody to find the table.</summary>
    [Fact]
    public void AnEmptyListOfRelaysIsRefused()
    {
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => new PartyChannel(new FakeRuntime(), AppId, relays: []));

        Assert.Equal("relays", refused.ParamName);
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

    /// <summary>
    /// How far out this machine's clock is, as the transport measured it, and whether it acted.
    /// </summary>
    /// <remarks>
    /// The relays refuse or filter out what a skewed clock signs, so a player a minute out never
    /// sees anybody. This is the only place a page can learn why.
    /// </remarks>
    [Fact]
    public async Task TheClockReadingIsRecorded()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        int changes = 0;
        channel.Changed += () => changes++;

        Assert.Null(channel.ClockOffset);
        Assert.False(channel.ClockCorrected);

        channel.ReceiveClock(-123_456, corrected: true);

        Assert.Equal(TimeSpan.FromMilliseconds(-123_456), channel.ClockOffset);
        Assert.True(channel.ClockCorrected);
        Assert.Equal(1, changes);
    }

    /// <summary>The offset is worked out from halves of a round trip and is kept to the millisecond.</summary>
    [Fact]
    public async Task TheClockReadingIsRoundedToWholeMilliseconds()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        channel.ReceiveClock(1_500.6, corrected: false);

        Assert.Equal(TimeSpan.FromMilliseconds(1_501), channel.ClockOffset);
        Assert.False(channel.ClockCorrected);
    }

    /// <summary>The largest believable offset is still a clock reading.</summary>
    [Fact]
    public async Task AClockReadingAtTheLimitIsKept()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        double maximumOffsetMs = TimeSpan.FromDays(36525).TotalMilliseconds;
        channel.ReceiveClock(maximumOffsetMs, corrected: true);

        Assert.Equal(TimeSpan.FromDays(36525), channel.ClockOffset);
        Assert.True(channel.ClockCorrected);
    }

    /// <summary>A reading just beyond the clock limit is not rounded back into range.</summary>
    [Fact]
    public async Task AClockReadingJustOutsideTheLimitIsIgnored()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveClock(1_000, corrected: false);

        int changes = 0;
        channel.Changed += () => changes++;

        double maximumOffsetMs = TimeSpan.FromDays(36525).TotalMilliseconds;
        channel.ReceiveClock(maximumOffsetMs + 0.4, corrected: true);

        Assert.Equal(TimeSpan.FromSeconds(1), channel.ClockOffset);
        Assert.False(channel.ClockCorrected);
        Assert.Equal(0, changes);
    }

    /// <summary>Telling the page the same thing twice does not redraw it twice.</summary>
    [Fact]
    public async Task AnUnchangedClockReadingRaisesNoChange()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveClock(42, corrected: false);

        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveClock(42, corrected: false);
        Assert.Equal(0, changes);

        channel.ReceiveClock(42, corrected: true);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A number from JavaScript that is not a believable clock is ignored rather than thrown on.
    /// </summary>
    /// <remarks>
    /// NaN and the infinities are ordinary JavaScript numbers, and <see cref="TimeSpan"/> throws on
    /// all three and on anything near its own limits. None of them is an answer worth showing.
    /// </remarks>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    [InlineData(1e16)]
    [InlineData(-1e16)]
    public async Task AnUnbelievableClockReadingIsIgnored(double offsetMs)
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveClock(1_000, corrected: false);

        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveClock(offsetMs, corrected: true);

        Assert.Equal(TimeSpan.FromSeconds(1), channel.ClockOffset);
        Assert.False(channel.ClockCorrected);
        Assert.Equal(0, changes);
    }

    /// <summary>A reading that arrives after leaving belongs to no table.</summary>
    [Fact]
    public void AClockReadingArrivingWhileAwayIsIgnored()
    {
        PartyChannel channel = new(new FakeRuntime(), AppId);

        channel.ReceiveClock(-90_000, corrected: true);

        Assert.Null(channel.ClockOffset);
        Assert.False(channel.ClockCorrected);
    }

    /// <summary>
    /// A peer the relays found and the browser could not reach is counted, not lost.
    /// </summary>
    /// <remarks>
    /// Without TURN, two networks that cannot meet directly look exactly like an empty table.
    /// Saying "somebody is there and cannot be reached" is the difference between a player trying
    /// another network and a player giving up.
    /// </remarks>
    [Fact]
    public async Task APeerThatCannotBeReachedIsCounted()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveJoinError("peer-1");

        Assert.Equal(1, channel.Unreachable);
        Assert.Equal(1, changes);
    }

    /// <summary>Trystero reports a peer again on every attempt; it is still one peer.</summary>
    [Fact]
    public async Task APeerThatFailsTwiceIsCountedOnce()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        channel.ReceiveJoinError("peer-1");

        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveJoinError("peer-1");

        Assert.Equal(1, channel.Unreachable);
        Assert.Equal(0, changes);
    }

    /// <summary>A peer that says hello has plainly been reached.</summary>
    [Fact]
    public async Task AHailClearsAnUnreachablePeer()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveJoinError("peer-1");
        channel.ReceiveJoinError("peer-2");

        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveHail("peer-1", Hail.From("Bran", DateTimeOffset.UnixEpoch).Write());

        Assert.Equal(1, channel.Unreachable);
        Assert.Equal(1, channel.Roster.Count);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A hail that cannot be read still came down a working connection.
    /// </summary>
    /// <remarks>
    /// The seat waits for a readable name, but the warning that nobody could be reached would be
    /// wrong as soon as anything at all had arrived.
    /// </remarks>
    [Fact]
    public async Task AnUnreadableHailStillClearsAnUnreachablePeer()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveJoinError("peer-1");

        channel.ReceiveHail("peer-1", "not a hail");

        Assert.Equal(0, channel.Unreachable);
        Assert.Equal(0, channel.Roster.Count);
    }

    /// <summary>A peer that failed once and connected later is no longer unreachable.</summary>
    [Fact]
    public async Task AnArrivalClearsAnUnreachablePeer()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveJoinError("peer-1");

        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveArrival("peer-1");

        Assert.Equal(0, channel.Unreachable);
        Assert.Equal(0, channel.Roster.Count);
        Assert.Equal(1, changes);
    }

    /// <summary>An arrival nobody was worried about changes nothing anybody can see.</summary>
    [Fact]
    public async Task AnArrivalOfAnUnknownPeerRaisesNoChange()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveArrival("peer-1");
        channel.ReceiveArrival(null);

        Assert.Equal(0, changes);
    }

    /// <summary>A peer that has gone is not still somebody who cannot be reached.</summary>
    [Fact]
    public async Task ADepartureClearsAnUnreachablePeer()
    {
        (PartyChannel channel, _) = await JoinedAsync();
        channel.ReceiveJoinError("peer-1");

        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveDeparture("peer-1");

        Assert.Equal(0, channel.Unreachable);
        Assert.Equal(1, changes);
    }

    /// <summary>
    /// A peer id that could not be anybody's is not counted.
    /// </summary>
    /// <remarks>
    /// The id came from another browser by way of the relays, so it is treated as input rather than
    /// as Trystero's word.
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnusablePeers))]
    public async Task AnUnusablePeerIdIsNotCounted(string? peer)
    {
        (PartyChannel channel, _) = await JoinedAsync();
        int changes = 0;
        channel.Changed += () => changes++;

        channel.ReceiveJoinError(peer);

        Assert.Equal(0, channel.Unreachable);
        Assert.Equal(0, changes);
    }

    public static TheoryData<string?> UnusablePeers => new()
    {
        null,
        "",
        "   ",
        "a b",
        "peer\u0000",
        "peer\n",
        new string('p', Roster.MaximumPeerLength + 1)
    };

    /// <summary>The longest id the roster would seat is long enough to count.</summary>
    [Fact]
    public async Task APeerIdAtTheLengthLimitIsCounted()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        channel.ReceiveJoinError(new string('p', Roster.MaximumPeerLength));

        Assert.Equal(1, channel.Unreachable);
    }

    /// <summary>A failure reported after leaving belongs to no table.</summary>
    [Fact]
    public void AJoinErrorArrivingWhileAwayIsIgnored()
    {
        PartyChannel channel = new(new FakeRuntime(), AppId);

        channel.ReceiveJoinError("peer-1");

        Assert.Equal(0, channel.Unreachable);
    }

    /// <summary>
    /// However many failures arrive, the count stops where the roster does.
    /// </summary>
    /// <remarks>
    /// Every id here is somebody else's to invent, so the set holding them cannot be left to grow.
    /// </remarks>
    [Fact]
    public async Task TheUnreachableCountIsBounded()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        for (int index = 0; index < Roster.MaximumPlayers * 4; index++)
        {
            channel.ReceiveJoinError(FormattableString.Invariant($"peer-{index}"));
        }

        Assert.Equal(Roster.MaximumPlayers, channel.Unreachable);

        channel.ReceiveDeparture("peer-0");
        channel.ReceiveJoinError("peer-new");

        Assert.Equal(Roster.MaximumPlayers, channel.Unreachable);
    }

    /// <summary>Leaving forgets the clock reading and who could not be reached.</summary>
    /// <remarks>
    /// The page keeps its correction, if it made one, and reports it again at the next table. What
    /// is cleared is only this channel's account of the last one.
    /// </remarks>
    [Fact]
    public async Task LeavingForgetsTheClockAndTheUnreachablePeers()
    {
        (PartyChannel channel, _) = await JoinedAsync();

        channel.ReceiveClock(-120_000, corrected: true);
        channel.ReceiveJoinError("peer-1");

        await channel.LeaveAsync();

        Assert.Null(channel.ClockOffset);
        Assert.False(channel.ClockCorrected);
        Assert.Equal(0, channel.Unreachable);
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
