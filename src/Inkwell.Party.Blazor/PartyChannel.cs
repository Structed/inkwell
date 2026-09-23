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
/// called strands every table code already written down. Pick one and keep it.
/// </para>
/// </param>
public sealed class PartyChannel(IJSRuntime js, string appId) : IAsyncDisposable
{
    /// <summary>Where the packaged module lands once the host has collected its static assets.</summary>
    private const string ModulePath = "./_content/Structed.Inkwell.Party.Blazor/js/party.js";

    private readonly IJSRuntime js = js;
    private readonly string app = string.IsNullOrWhiteSpace(appId)
        ? throw new ArgumentException("A party channel needs an app id to namespace its signalling.", nameof(appId))
        : appId;
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

    /// <summary>Raised whenever anything on screen would need to change.</summary>
    public event Action? Changed;

    public bool IsJoined => Status is PartyStatus.Joining or PartyStatus.Searching or PartyStatus.Open;

    /// <summary>Joins a table, leaving any current one first.</summary>
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

            await module.InvokeAsync<string>("join", app, parsed, self);
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
    /// </remarks>
    [JSInvokable]
    public void ReceiveHail(string peer, string json)
    {
        if (Hail.TryRead(json, out Hail hail) && Roster.Greet(peer, hail))
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Clears the seat of somebody who has gone.</summary>
    [JSInvokable]
    public void ReceiveDeparture(string peer)
    {
        if (Roster.Leave(peer))
        {
            Changed?.Invoke();
        }
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
