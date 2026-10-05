using Microsoft.JSInterop;
using Structed.Inkwell.Dice;

namespace Structed.Inkwell.Party.Blazor;

/// <summary>How the connection to the table is going.</summary>
public enum PartyStatus
{
    /// <summary>Not at a table.</summary>
    Away,

    /// <summary>Opening the room.</summary>
    Joining,

    /// <summary>At a table, but no relay is answering, so nobody new can find us.</summary>
    Searching,

    /// <summary>At a table and findable.</summary>
    Open,

    /// <summary>The browser would not do it at all.</summary>
    Failed
}

/// <summary>
/// The page's end of the dice channel.
/// </summary>
/// <remarks>
/// <para>
/// Owns the JavaScript module, the log, and the rule that decides what leaves the machine. Rolls
/// arrive here as strings from other people's browsers and are read by
/// <see cref="RollMessage.TryRead"/> before anything is done with them; nothing on the JavaScript
/// side is trusted to have checked anything, because nothing on the JavaScript side knows what a
/// roll is.
/// </para>
/// <para>
/// Private rolls never reach the channel at all. <see cref="RollAsync"/> ghosts them before sending
/// and keeps the full version only in the local log, and <see cref="Replay"/> ghosts again on the
/// way out. Two independent chances to get it right, because getting it wrong is the one failure
/// here that cannot be undone by refreshing.
/// </para>
/// <para>
/// The <see cref="Roster"/> is the other half: names arrive as hails, keyed by the connection they
/// came in on rather than by anything the message claims, so the table can show who is at it before
/// anybody has rolled.
/// </para>
/// <para>
/// An empty table says nothing about why it is empty, so the channel also carries the two causes
/// the transport can see: a machine clock the relays will not accept (<see cref="ClockOffset"/>,
/// <see cref="ClockCorrected"/>) and peers that were found but could not be connected to
/// (<see cref="Unreachable"/>).
/// </para>
/// </remarks>
/// <param name="js">The page's JavaScript runtime.</param>
/// <param name="appId">
/// <para>
/// Namespaces the signalling, so two unrelated apps sharing a relay never meet even if somebody
/// picks the same table code by accident.
/// </para>
/// <para>
/// Asked for rather than invented, because it is a compatibility surface: everybody who is to sit
/// at the same table must pass the same string, and an app that changes its mind about what it is
/// called strands every table code already written down. Pick one and keep it — until the
/// transport underneath changes what it sends between browsers, as Trystero 0.26.0 did in
/// Inkwell 0.4.0. Then change it on purpose, so builds on either side of the change each meet only
/// their own kind rather than half-connecting to tables they cannot talk to.
/// </para>
/// </param>
/// <param name="relays">
/// <para>
/// The Nostr relays the table is signalled through, as <c>wss://</c> (or <c>ws://</c>) URLs, or
/// <see langword="null"/> to leave the choice to Trystero.
/// </para>
/// <para>
/// Left to itself, Trystero takes five relays from a list compiled into its bundle, shuffled by the
/// app id alone — so the draw is the same for every table the app ever opens, and a relay that has
/// died stays in it however often anybody rejoins. Naming them here replaces that draw outright:
/// every relay given is used, and nothing else is.
/// </para>
/// <para>
/// Like the app id, this describes the app rather than the table. Relays are how players find each
/// other, so everybody who is to sit at the same table must share at least one of them.
/// </para>
/// </param>
public sealed class PartyChannel(IJSRuntime js, string appId, IEnumerable<string>? relays = null) : IAsyncDisposable
{
    /// <summary>Where the packaged module lands once the host has collected its static assets.</summary>
    private const string ModulePath = "./_content/Structed.Inkwell.Party.Blazor/js/party.js";

    /// <summary>The furthest out a clock reading from the transport may claim to be.</summary>
    /// <remarks>
    /// A machine whose clock battery has died wakes up at its firmware's epoch — 1970, 1980 or 2000
    /// — which is decades out and still well inside a century. Anything beyond that is not a clock
    /// that is wrong but a reading that is broken, and is not worth putting on screen.
    /// </remarks>
    private static readonly TimeSpan MaximumClockOffset = TimeSpan.FromDays(36525);

    private readonly IJSRuntime js = js;
    private readonly string app = string.IsNullOrWhiteSpace(appId)
        ? throw new ArgumentException("A party channel needs an app id to namespace its signalling.", nameof(appId))
        : appId;
    private readonly string[]? relays = ReadRelays(relays);
    private readonly HashSet<string> unreachable = new(StringComparer.Ordinal);
    private IJSObjectReference? module;
    private DotNetObjectReference<PartyChannel>? self;

    /// <summary>What the table has rolled.</summary>
    public RollLog Log { get; } = new();

    /// <summary>Who else is at the table.</summary>
    public Roster Roster { get; } = new();

    public PartyStatus Status { get; private set; } = PartyStatus.Away;

    /// <summary>The table code, or empty when away.</summary>
    public string Code { get; private set; } = "";

    /// <summary>How many other people are connected.</summary>
    public int Peers { get; private set; }

    /// <summary>What this player is called, as it will appear to everybody else.</summary>
    public string Player { get; set; } = "";

    /// <summary>
    /// How far this machine's clock was found to be from the site's, or <see langword="null"/> if
    /// it has not been measured.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Positive when this machine is behind. Measured once per page, from the <c>Date</c> header of
    /// a request for the page itself, and reported again at every table joined on that page.
    /// </para>
    /// <para>
    /// The table is signalled through Nostr relays, which judge an event by the clock that signed
    /// it. Probed live, the strfry relays answer <c>"invalid: ephemeral event expired"</c> to an
    /// event more than sixty seconds old and <c>"invalid: created_at too late"</c> to one more than
    /// fifteen minutes ahead, and a relay that accepts a ninety-second-old event still leaves it out
    /// of a subscription for events since now. So a player whose clock is a minute out sits at the
    /// table unseen and unseeing, with every relay saying so to nobody. This is how a page can tell
    /// them why.
    /// </para>
    /// </remarks>
    public TimeSpan? ClockOffset { get; private set; }

    /// <summary>Whether the page is signalling on the site's clock rather than the machine's own.</summary>
    /// <remarks>
    /// <para>
    /// The transport corrects a clock ten seconds or more out, before it first joins a table, and
    /// keeps the correction for as long as the page is open: Trystero schedules by differences of
    /// the time, so moving the clock under timers it has already set would stretch or shrink every
    /// one of them by the whole offset.
    /// </para>
    /// <para>
    /// The consequence is that a clock measured too late for the first join is reported here and
    /// not corrected. A large <see cref="ClockOffset"/> with this <see langword="false"/> is a
    /// player who will probably not find anybody, and whom reloading the page — or setting the
    /// clock — would fix.
    /// </para>
    /// </remarks>
    public bool ClockCorrected { get; private set; }

    /// <summary>
    /// How many peers were found at the table and could not be connected to, and have not
    /// connected since.
    /// </summary>
    /// <remarks>
    /// Two browsers that have exchanged offers through the relays and still cannot open a direct
    /// connection — almost always two networks that need a TURN server, with none configured — are
    /// otherwise indistinguishable from an empty table. Trystero reports it once per attempt and
    /// nowhere a player would see. A peer is counted until it connects, says hello or leaves, or
    /// until this player leaves the table.
    /// </remarks>
    public int Unreachable => unreachable.Count;

    /// <summary>Raised whenever anything on screen would need to change.</summary>
    public event Action? Changed;

    public bool IsJoined => Status is PartyStatus.Joining or PartyStatus.Searching or PartyStatus.Open;

    /// <summary>Joins a table, leaving any current one first.</summary>
    /// <remarks>
    /// The first join on a page waits up to three seconds for the transport to measure the clock
    /// (see <see cref="ClockOffset"/>), because a correction has to be in place before the transport
    /// first signals or not at all. Later joins on the same page reuse that measurement.
    /// </remarks>
    public async Task JoinAsync(string code)
    {
        if (!TableCode.TryParse(code, out string parsed))
        {
            return;
        }

        await LeaveAsync();

        Code = parsed;
        Status = PartyStatus.Joining;
        Changed?.Invoke();

        try
        {
            module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            self ??= DotNetObjectReference.Create(this);

            await module.InvokeAsync<string>("join", app, relays, parsed, self);
        }
        catch (Exception error) when (error is JSException or InvalidOperationException)
        {
            // A browser without WebRTC, a blocked relay, an extension that ate the module: all of
            // them arrive here, and all of them mean the same thing to a player.
            Status = PartyStatus.Failed;
            Changed?.Invoke();
        }
    }

    /// <summary>Leaves the table and forgets what was rolled at it.</summary>
    public async Task LeaveAsync()
    {
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("leave");
            }
            catch (Exception error) when (error is JSException or JSDisconnectedException)
            {
                // Leaving a room that has already gone is not a failure worth reporting.
            }
        }

        Code = "";
        Peers = 0;
        Status = PartyStatus.Away;
        ClockOffset = null;
        ClockCorrected = false;
        unreachable.Clear();
        Log.Clear();
        Roster.Clear();
        Changed?.Invoke();
    }

    /// <summary>
    /// Changes what this player is called, and tells the table if there is one.
    /// </summary>
    /// <remarks>
    /// A name typed after joining is the common case, not the exception: people open the link,
    /// land at the table and then decide what to call themselves. Re-hailing on every change keeps
    /// the other screens right without anybody having to rejoin.
    /// </remarks>
    public async Task RenameAsync(string? name)
    {
        Player = name ?? "";

        if (IsJoined && module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("greet", Greeting());
            }
            catch (Exception error) when (error is JSException or JSDisconnectedException)
            {
                // A name that did not reach the others is a cosmetic loss, and the next hail — on
                // the next peer to arrive — fixes it.
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Announces a roll: to the table, or only to this screen.
    /// </summary>
    /// <returns>The entry as it was added locally, so the roller sees their own dice either way.</returns>
    public async Task<RollMessage> RollAsync(
        RollOutcome outcome,
        RollReading? reading,
        bool secret,
        string preset = "")
    {
        RollMessage message = RollMessage.From(
            Guid.NewGuid().ToString("n"),
            Player,
            outcome,
            reading,
            secret,
            DateTimeOffset.UtcNow,
            preset);

        Log.Add(message, isMine: true);

        if (IsJoined && module is not null)
        {
            try
            {
                await module.InvokeAsync<bool>("send", secret ? message.Ghost().Write() : message.Write());
            }
            catch (Exception error) when (error is JSException or JSDisconnectedException)
            {
                // The roll is on this screen regardless. Losing it in transit is the transport's
                // business, and the status line is already saying how the transport is doing.
            }
        }

        Changed?.Invoke();
        return message;
    }

    /// <summary>Takes one roll from another player.</summary>
    [JSInvokable]
    public void ReceiveRoll(string json)
    {
        if (RollMessage.TryRead(json, out RollMessage message) && Log.Add(message))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Takes a history from another player, merging it with what is already here.</summary>
    [JSInvokable]
    public void ReceiveHistory(string json)
    {
        if (Log.Merge(RollHistory.Read(json)) > 0)
        {
            Changed?.Invoke();
        }
    }

    [JSInvokable]
    public void ReceivePeers(int count)
    {
        if (Peers == count)
        {
            return;
        }

        Peers = Math.Max(count, 0);
        Changed?.Invoke();
    }

    /// <summary>Takes a name from the player on one connection.</summary>
    /// <remarks>
    /// The connection is the transport's word, not the sender's: a hail says what somebody is
    /// called and never which seat it belongs to, so nobody can rename a player across the table.
    /// A hail also proves the connection works, so a peer that sends one is no longer unreachable.
    /// </remarks>
    [JSInvokable]
    public void ReceiveHail(string peer, string json)
    {
        bool reached = Reach(peer);

        if ((Hail.TryRead(json, out Hail hail) && Roster.Greet(peer, hail)) | reached)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Clears the seat of somebody who has gone.</summary>
    [JSInvokable]
    public void ReceiveDeparture(string peer)
    {
        if (Roster.Leave(peer) | Reach(peer))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Notes that the transport has connected to a peer.</summary>
    /// <remarks>
    /// Nothing is seated by this: a seat needs a name, and a name only comes in a hail. It is here
    /// so that a peer that failed to connect once and then managed it is no longer counted in
    /// <see cref="Unreachable"/>, even if its hail has not arrived yet.
    /// </remarks>
    [JSInvokable]
    public void ReceiveArrival(string? peer)
    {
        if (Reach(peer))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Counts a peer the transport found and could not connect to.</summary>
    /// <remarks>
    /// The peer id comes from another browser by way of the relays, so it is held to the same
    /// shape as a seat's and the set it lands in is capped at the size of the roster: a peer that
    /// has found a way to fail as a thousand connections costs a capped count rather than the page.
    /// </remarks>
    [JSInvokable]
    public void ReceiveJoinError(string? peer)
    {
        if (Status is PartyStatus.Away
            || ReadPeer(peer) is not { } read
            || (unreachable.Count >= Roster.MaximumPlayers && !unreachable.Contains(read)))
        {
            return;
        }

        if (unreachable.Add(read))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Takes the transport's measurement of this machine's clock.</summary>
    /// <param name="offsetMs">
    /// The site's time less this machine's, in milliseconds. Positive when this machine is behind.
    /// </param>
    /// <param name="corrected">Whether the page is now signalling on the site's time.</param>
    /// <remarks>
    /// JavaScript numbers include NaN and the infinities, and nothing on that side of the boundary
    /// is trusted to have ruled them out; a reading that is not a believable clock is ignored rather
    /// than turned into a <see cref="TimeSpan"/> that throws.
    /// </remarks>
    [JSInvokable]
    public void ReceiveClock(double offsetMs, bool corrected)
    {
        if (Status is PartyStatus.Away
            || !double.IsFinite(offsetMs)
            || Math.Abs(offsetMs) > MaximumClockOffset.TotalMilliseconds)
        {
            return;
        }

        TimeSpan offset = TimeSpan.FromMilliseconds(Math.Round(offsetMs));

        if (ClockOffset == offset && ClockCorrected == corrected)
        {
            return;
        }

        ClockOffset = offset;
        ClockCorrected = corrected;
        Changed?.Invoke();
    }

    /// <summary>Says what this player is called, for sending to somebody who has just arrived.</summary>
    /// <remarks>
    /// Called from JavaScript, which does not decide what goes in it, in the same way as
    /// <see cref="Replay"/>. The transport asks for a greeting and relays whatever it is handed.
    /// </remarks>
    [JSInvokable]
    public string Greeting() => Hail.From(Player, DateTimeOffset.UtcNow).Write();

    [JSInvokable]
    public void ReceiveStatus(string status)
    {
        PartyStatus read = status switch
        {
            "joining" => PartyStatus.Joining,
            "searching" => PartyStatus.Searching,
            "open" => PartyStatus.Open,
            _ => PartyStatus.Failed
        };

        if (Status == read || Status is PartyStatus.Away)
        {
            return;
        }

        Status = read;
        Changed?.Invoke();
    }

    /// <summary>Answers a newcomer asking what they missed.</summary>
    /// <remarks>
    /// Called from JavaScript, which does not decide what goes in it. The log ghosts private rolls
    /// on the way out, so this is safe to answer without checking whose rolls are in it.
    /// </remarks>
    [JSInvokable]
    public string Replay() => RollHistory.Write(Log.Replay());

    /// <summary>Checks the host's relays once, up front, rather than at a table nobody can find.</summary>
    /// <remarks>
    /// A relay that is not a WebSocket URL fails exactly as quietly as one that has been deleted, so
    /// it is refused here, where the mistake is still the host's and can still be read. An empty
    /// list is refused for the same reason: Trystero would take it at its word and signal through
    /// nothing, which is a table that looks open and can never be joined.
    /// </remarks>
    private static string[]? ReadRelays(IEnumerable<string>? relays)
    {
        if (relays is null)
        {
            return null;
        }

        List<string> read = [];

        foreach (string? relay in relays)
        {
            string url = relay?.Trim() ?? "";

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || uri.Scheme is not ("wss" or "ws")
                || uri.Host.Length == 0)
            {
                throw new ArgumentException(
                    $"A relay has to be a wss:// or ws:// URL, and '{relay}' is not one.",
                    nameof(relays));
            }

            if (!read.Contains(url, StringComparer.Ordinal))
            {
                read.Add(url);
            }
        }

        return read.Count > 0
            ? [.. read]
            : throw new ArgumentException(
                "A party channel given no relays could never be found. Pass null to use Trystero's own.",
                nameof(relays));
    }

    /// <summary>Stops counting a peer as unreachable, if it was.</summary>
    private bool Reach(string? peer) => peer is not null && unreachable.Remove(peer);

    /// <summary>Checks a peer id handed over by the transport before it is kept.</summary>
    /// <remarks>
    /// Trystero's ids are short and plain, but this one has come from another browser by way of the
    /// relays, and a set that holds onto it is a set somebody else can fill. The length limit is the
    /// roster's own. An id that breaks it, or carries spaces or control characters, is refused
    /// outright rather than cleaned the way the roster cleans one, because a cleaned id would no
    /// longer match the connection it came from and could never be cleared again.
    /// </remarks>
    private static string? ReadPeer(string? peer)
    {
        if (string.IsNullOrWhiteSpace(peer) || peer.Length > Roster.MaximumPeerLength)
        {
            return null;
        }

        foreach (char letter in peer)
        {
            if (char.IsWhiteSpace(letter) || char.IsControl(letter))
            {
                return null;
            }
        }

        return peer;
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("leave");
                await module.DisposeAsync();
            }
            catch (Exception error) when (error is JSException or JSDisconnectedException)
            {
                // The page is going away, which is the only reason this is being called.
            }
        }

        module = null;
        self?.Dispose();
        self = null;
    }
}
