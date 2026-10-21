// The dice channel: one room, a handful of opaque messages, and nothing that knows what a roll is.
//
// Everything with a rule in it lives in C#. This file opens a peer-to-peer room, relays opaque
// strings across it, and refuses anything that arrives too big or too fast. It has no vocabulary of
// its own — no dice, no game, no wording — which is what lets the interesting half stay testable
// without a browser, and lets this half be swapped for a different transport without touching it.

import { joinRoom, selfId, getRelaySockets } from './trystero-nostr.js';

// A roll is a few hundred bytes and a replayed history a few thousand. Anything beyond this is not
// a dice message, and is dropped before it is looked at rather than after.
const maximumRollBytes = 4096;
const maximumHistoryBytes = 65536;
const maximumHailBytes = 512;

// A peer sending more than this is either broken or trying it on. Twelve rolls in ten seconds is
// already faster than anyone rolls dice, and the limit is per peer so one loud peer cannot drown
// out the rest of the table.
const rollAllowance = 12;
const historyAllowance = 3;
const hailAllowance = 12;
const allowanceWindow = 10000;

// How often this player says their name again to the whole table without being asked. A hail sent
// once is a hail that can be lost once, and a lost one leaves a seat saying hello for good. Every
// thirty seconds is a rounding error against the hail allowance, and an identical name is ignored
// at the other end.
const rehailInterval = 30000;

// The signalling is Nostr, and Nostr relays judge an event by the clock that signed it. Probed live,
// most refuse an ephemeral event more than a minute old ("invalid: ephemeral event expired", or
// "invalid: created_at too early"), and some refuse one far in the future ("invalid: created_at too
// late") — though how far is each relay's own choice. The ones that take a skewed event anyway still
// filter by `since`: a slow clock's events fall before everybody else's subscription, and a fast
// clock subscribes from a moment nobody else has reached, so it never hears the replies. A player
// whose clock is a minute out therefore meets nobody, on any relay, without anything saying why. Ten
// seconds is well inside every limit seen, and far outside what measuring over HTTP gets wrong.
const clockTolerance = 10000;

// How long a join waits to hear the time before going ahead without it. An answer that takes longer
// is still put into effect when it comes.
const clockPatience = 3000;

// How often the clock is looked at, for as long as anything depends on it.
const clockWatch = 2000;

// How far the machine's clock may move against the page's steady one before the time is asked for
// again. Syncing it, setting it by hand, or waking from a sleep the steady clock slept through moves
// it by far more than this; NTP slewing it would take the best part of an hour to.
const clockStep = 2000;

// How far a new measurement has to be from the correction in force before the correction moves. The
// `Date` header only counts whole seconds, so two honest measurements a minute apart can differ by
// most of one, and following each of them would nudge every timer on the page for nothing.
const clockHysteresis = 2000;

// How often the time is asked for anyway. A clock that merely runs fast or slow drifts along with
// the page's steady clock rather than against it, so watching one against the other never sees it.
const clockRefresh = 600000;

// How soon a question that came back with nothing is asked again. It doubles with each failure in a
// row, up to `clockRefresh`.
const clockRetry = 2000;

// A fixed floor: the day this check was written. No server telling the truth can send a date before
// it, so a header that does is a broken proxy, and believing it would wind the page back years. It
// only ever has to be earlier than now, so it never needs moving when this file changes.
const earliestPlausibleDate = Date.UTC(2026, 9, 1);

// The machine's own clock, taken before anything here has had the chance to correct it.
const realNow = Date.now.bind(Date);

// The page's steady clock. It counts from when the page opened, and nothing done to the machine's
// clock moves it — which makes the difference between the two the way to see that being done.
const steadyNow = performance.now.bind(performance);

let table = null;

// What the page knows about the time. See `settle` for what is done with it.
//
// `measured` is the last offset found, in milliseconds, or null when none has been; `drift` is the
// machine's clock less the steady one when it was found, and `takenAt` when, by the steady clock.
// `correction` is what the page's `Date.now` adds to the machine's clock, and is zero when it adds
// nothing. `pending` is the question while it is out, and `failures` and `retryAt` say how soon to
// ask again after one that came back with nothing; `missedDrift` is what `drift` was when it did,
// so that a step since then can be told from the same question being asked again.
let measured = null;
let drift = 0;
let takenAt = 0;
let correction = 0;
let installed = false;
let pending = null;
let failures = 0;
let retryAt = 0;
let missedDrift = 0;
let vigil = null;
let roused = null;

/// Joins a table. Any table already open is left first, so there is only ever one.
//
// `appId` namespaces the signalling so two unrelated apps using the same relays never meet, even if
// somebody picks the same room code by accident. It is supplied by the caller rather than fixed
// here, because this file is shared and the app it is running inside is the only thing that knows
// which app it is. `relays` is the same kind of fact — which relays the app signals through — and
// is null when the app has left that to Trystero.
export async function join(appId, relays, code, handler) {
    leave();

    // The table is claimed before the clock is asked, so that leaving — or joining somewhere else —
    // while the answer is still on its way closes this attempt rather than racing it.
    const current = {
        room: null,
        handler,
        roll: null,
        ask: null,
        tale: null,
        hail: null,
        allowances: new Map(),
        asked: new Set(),
        present: new Set(),
        closed: false
    };

    table = current;

    // A clock that has never been measured, has been seen to move since, or was measured a while ago
    // is asked about before the room is opened, so that Trystero's first signatures and first
    // subscriptions carry the right time.
    await hear();

    if (current.closed) {
        return selfId;
    }

    // A peer that exchanged offers with us and still could not be reached — almost always two
    // networks that need a TURN server and have none — is otherwise indistinguishable from nobody
    // being there. Trystero says so through this callback and nowhere else.
    const room = joinRoom(settings(appId, relays), code, {
        onJoinError: details => {
            const peer = who(details);

            if (peer && !current.present.has(peer)) {
                call(current, 'ReceiveJoinError', peer);
            }
        }
    });

    // Trystero hands back one object per action, and the receiving side is a settable property
    // rather than a register-a-callback function. The sender is `send(payload, { target })`, and
    // the handler is called with `(payload, { peerId })` — the peer arrives in a bag of metadata,
    // not as a bare second argument.
    const roll = room.makeAction('roll');
    const ask = room.makeAction('ask');
    const tale = room.makeAction('tale');
    const hail = room.makeAction('hail');

    Object.assign(current, { room, roll, ask, tale, hail });

    tell(current);

    roll.onMessage = (data, from) => {
        const peer = who(from);

        if (!peer || !allow(current, peer, 'roll', rollAllowance)) {
            return;
        }

        const json = text(data, maximumRollBytes);

        if (json) {
            call(current, 'ReceiveRoll', json);
        }
    };

    // Somebody has just arrived and wants to know what they missed. The answer comes from C#, which
    // is where the ghosting of private rolls happens; this file never decides what is safe to tell.
    ask.onMessage = async (_, from) => {
        const peer = who(from);

        if (!peer || !allow(current, peer, 'ask', historyAllowance)) {
            return;
        }

        try {
            const history = await current.handler.invokeMethodAsync('Replay');

            if (history && !current.closed) {
                post(current.tale, history, peer);
            }
        } catch {
            // The page has gone. Nothing to answer with, and nothing to report it to.
        }
    };

    tale.onMessage = (data, from) => {
        const peer = who(from);

        if (!peer || !allow(current, peer, 'tale', historyAllowance)) {
            return;
        }

        const json = text(data, maximumHistoryBytes);

        if (json) {
            call(current, 'ReceiveHistory', json);
        }
    };

    // Somebody saying what they are called. Which connection it arrived on is the transport's
    // business and is passed along with it, because the message itself does not say.
    hail.onMessage = (data, from) => {
        const peer = who(from);

        if (!peer || !allow(current, peer, 'hail', hailAllowance)) {
            return;
        }

        const json = text(data, maximumHailBytes);

        if (json) {
            call(current, 'ReceiveHail', peer, json);
        }
    };

    room.onPeerJoin = async peer => {
        peers(current);
        await arrive(current, peer);
    };

    room.onPeerLeave = peer => {
        depart(current, peer);
        peers(current);
    };

    call(current, 'ReceiveStatus', 'joining');
    watch(current);
    rehail(current);
    keepWatch();

    return selfId;
}

/// What Trystero is told about the app: its id, and its relays when the app has named any.
//
// Left without `relayConfig`, Trystero takes five relays from a list compiled into its bundle,
// shuffled by the app id alone, so the draw never changes and a dead relay in it stays there. Given
// `urls`, it uses exactly those and applies no redundancy limit to them. An empty list would be
// taken at its word and signal through nothing, so it is treated as not having named any; C# has
// already refused one, and this is only the second line.
function settings(appId, relays) {
    const urls = Array.isArray(relays)
        ? relays.filter(url => typeof url === 'string' && url.length > 0)
        : [];

    return urls.length > 0 ? { appId, relayConfig: { urls } } : { appId };
}

/// Sends one roll to everybody at the table.
export function send(json) {
    if (!table || table.closed || !table.roll || typeof json !== 'string' || json.length > maximumRollBytes) {
        return false;
    }

    return post(table.roll, json);
}

/// Tells everybody at the table what this player is now called.
export function greet(json) {
    if (!table || table.closed || !table.hail || typeof json !== 'string' || json.length > maximumHailBytes) {
        return false;
    }

    return post(table.hail, json);
}

/// Leaves the table. Safe to call when there is no table, and safe to call twice.
//
// The clock correction, if there is one, stays: the machine's clock is as wrong as it was, and the
// page goes on following it while away from a table. See `settle`.
export function leave() {
    if (!table) {
        return;
    }

    const closing = table;
    table = null;
    closing.closed = true;

    if (closing.watcher) {
        clearInterval(closing.watcher);
    }

    if (closing.hailer) {
        clearInterval(closing.hailer);
    }

    if (closing.woken && typeof document !== 'undefined') {
        document.removeEventListener('visibilitychange', closing.woken);
    }

    try {
        const left = closing.room?.leave();

        if (left && typeof left.catch === 'function') {
            left.catch(() => { });
        }
    } catch {
        // Already gone.
    }

    keepWatch();
}

/// Reports how many relays are actually carrying the signalling, and how many peers are present.
//
// Relays are how peers find each other, not how rolls travel — once a connection is made the dice
// go directly between browsers. But a table with no relay is a table nobody new can join, and that
// is worth saying out loud rather than leaving as an unexplained silence.
//
// The same look checks the seats against the connections Trystero actually holds, so that a
// departure it never announced, or an arrival it never announced, is caught within a couple of
// seconds rather than left on screen until somebody reloads.
function watch(current) {
    let last = '';

    const look = () => {
        if (current.closed) {
            return;
        }

        let open = 0;

        try {
            const sockets = getRelaySockets();

            for (const url of Object.keys(sockets)) {
                if (sockets[url] && sockets[url].readyState === 1) {
                    open++;
                }
            }
        } catch {
            open = 0;
        }

        const status = open > 0 ? 'open' : 'searching';

        if (status !== last) {
            last = status;
            call(current, 'ReceiveStatus', status);
        }

        reconcile(current);
    };

    look();
    current.watcher = setInterval(look, 2000);
    current.look = look;
}

/// Lines up who we think is here with who Trystero says is connected.
//
// Ghost seats and missing seats both come from one missed event. Trystero's own list of peers is
// the thing that decides whether a message can actually reach somebody, so it wins.
function reconcile(current) {
    let connected;

    try {
        connected = new Set(Object.keys(current.room.getPeers()));
    } catch {
        return;
    }

    const gone = [...current.present].filter(peer => !connected.has(peer));
    const come = [...connected].filter(peer => !current.present.has(peer));

    for (const peer of gone) {
        depart(current, peer);
    }

    for (const peer of come) {
        arrive(current, peer);
    }

    if (gone.length > 0 || come.length > 0) {
        peers(current);
    }
}

/// Says this player's name again, to everybody, every so often and whenever the page comes back.
//
// A hidden tab has its timers throttled to a crawl, so coming back into view is exactly when the
// other screens are most likely to have lost track of us, and exactly when nobody wants to wait
// half a minute to be seen.
function rehail(current) {
    current.hailer = setInterval(() => hello(current), rehailInterval);

    if (typeof document === 'undefined') {
        return;
    }

    current.woken = () => {
        if (document.visibilityState === 'visible' && !current.closed) {
            current.look?.();
            hello(current);
        }
    };

    document.addEventListener('visibilitychange', current.woken);
}

/// Seats somebody Trystero has connected us to.
//
// Asked once per peer, by both sides. The newcomer gets the history they wanted; the established
// player gets a batch they already have and discards it. Symmetry is cheaper here than working out
// which of us arrived first.
async function arrive(current, peer) {
    if (!current.present.has(peer)) {
        current.present.add(peer);
        call(current, 'ReceiveArrival', peer);
    }

    if (!current.asked.has(peer)) {
        current.asked.add(peer);
        post(current.ask, '', peer);
    }

    await hello(current, peer);
}

/// Clears away everything held about somebody who has gone, however we found out.
function depart(current, peer) {
    current.present.delete(peer);
    current.asked.delete(peer);

    const prefix = peer + '\u0000';

    for (const key of [...current.allowances.keys()]) {
        if (key.startsWith(prefix)) {
            current.allowances.delete(key);
        }
    }

    call(current, 'ReceiveDeparture', peer);
}

function peers(current) {
    let count = 0;

    try {
        count = Object.keys(current.room.getPeers()).length;
    } catch {
        count = 0;
    }

    call(current, 'ReceivePeers', count);
}

/// Introduces this player to one peer, or to everybody, in whatever words C# chooses.
//
// Both sides do this when they see each other, for the same reason both sides ask for the history:
// it costs one small message and saves working out which of the two arrived first.
async function hello(current, peer) {
    if (current.closed || (!peer && current.present.size === 0)) {
        return;
    }

    try {
        const greeting = await current.handler.invokeMethodAsync('Greeting');

        if (greeting && !current.closed) {
            post(current.hail, greeting, peer);
        }
    } catch {
        // The page has gone. There is nobody left to introduce.
    }
}

/// Asks the site what time it is.
//
// The site is the one clock every player at a table has already agreed to trust, by loading it.
// A HEAD request for the page itself, with a query nothing else uses and `no-store`, goes past
// the browser's cache — and past the service worker Blazor's template installs, which only ever
// answers GETs — to a server that stamps a fresh `Date` on its answer. GitHub Pages does even when
// its CDN answers from cache, and so does Cloudflare Pages.
//
// The header is only good to the second and is truncated, not rounded, so the true time is half a
// second past it on average; the round trip is split down the middle for the same reason, timed on
// the steady clock. Both ends are read on both clocks, too: if the machine's clock moved while the
// question was out, there is no telling which side of the move the answer belongs to, and it is
// thrown away rather than half believed.
//
// Resolves to the offset in milliseconds, with the drift, the moment it was found at and whether the
// round trip was slow enough to leave it a second or more uncertain, or to null when there is no
// telling.
async function inquire() {
    try {
        if (typeof document === 'undefined' || typeof fetch !== 'function') {
            return null;
        }

        const url = new URL(document.baseURI);
        url.hash = '';
        url.searchParams.set('inkwell-clock', realNow().toString(36) + Math.random().toString(36).slice(2));

        const before = realNow();
        const sent = steadyNow();
        const response = await fetch(url, { method: 'HEAD', cache: 'no-store' });
        const received = steadyNow();
        const after = realNow();
        const said = Date.parse(response.headers.get('Date') ?? '');

        if (!Number.isFinite(said) || said < earliestPlausibleDate) {
            return null;
        }

        if (Math.abs(after - received - (before - sent)) >= clockStep) {
            return null;
        }

        return {
            offset: Math.round(said + 500 - (before + (received - sent) / 2)),
            drift: after - received,
            at: received,
            rough: received - sent >= clockStep
        };
    } catch {
        // Offline, blocked, or a host that sends no Date. The page goes on with whatever it had,
        // which for nearly everybody is the machine's own clock and right.
        return null;
    }
}

/// Asks the time again, unless the question is already out, and puts the answer into effect.
function measure() {
    pending ??= inquire()
        .then(found => found ? take(found) : miss())
        .catch(() => { })
        .finally(() => {
            pending = null;
        });

    return pending;
}

/// Puts a new measurement into effect, and tells the table.
//
// A rough one — from a round trip so slow that the answer could belong anywhere in it — is still
// far better than a clock minutes out, and is used; but it is asked again, backing off like a
// failure, until an answer comes back sharp.
function take(found) {
    measured = found.offset;
    drift = found.drift;
    takenAt = found.at;

    if (found.rough) {
        takenAt = -Infinity;
        miss();
    } else {
        failures = 0;
        retryAt = 0;
    }

    settle(found.offset);

    if (table?.room) {
        tell(table);
    }

    keepWatch();
}

/// Notes a question that came back with nothing, or with too little to go on, and puts off the next.
//
// Whatever correction is in force stays. It was right when it was measured, and guessing at a
// replacement — by trusting the steady clock across a step, say, which some platforms stop during
// sleep — would be wrong in ways nothing could see.
function miss() {
    const now = steadyNow();

    failures++;
    retryAt = now + Math.min(clockRetry * 2 ** (failures - 1), clockRefresh);
    missedDrift = realNow() - now;
}

/// Whether the time has to be asked for again: it never has been, the machine's clock has moved
/// since, or it was a while ago.
function due() {
    const now = steadyNow();

    return measured === null
        || Math.abs(realNow() - now - drift) >= clockStep
        || now - takenAt >= clockRefresh;
}

/// Looks at the clock, and asks the time again if it has to and has not just failed to.
//
// The backoff is for asking the same question again, not for a new one. A clock that has moved since
// the last failure — synced, set by hand, or woken from sleep, often just before the network comes
// back — is asked about at once, and the backoff starts again from the beginning; otherwise a page
// that had been failing for a while would leave a correction it knows to be stale in force for up to
// `clockRefresh`. An offline page still asks once per step and then backs off, rather than every look.
function check() {
    if (pending) {
        return;
    }

    const now = steadyNow();

    if (now < retryAt) {
        if (Math.abs(realNow() - now - missedDrift) < clockStep) {
            return;
        }

        failures = 0;
        retryAt = 0;
    }

    if (due()) {
        measure();
    }
}

/// Asks the time before a join if it has to, and waits for the answer a little while and no longer.
//
// A question already out, from the watch, is waited for in the same way rather than asked twice. A
// failure's backoff is not: somebody has just asked to join, and the question is cheap.
async function hear() {
    if (!pending && !due()) {
        return;
    }

    let timer = null;

    try {
        await Promise.race([
            measure(),
            new Promise(resolve => {
                timer = setTimeout(resolve, clockPatience);
            })
        ]);
    } finally {
        clearTimeout(timer);
    }
}

/// Watches the clock for as long as anything depends on it: a table, or a page running on the
/// site's time, which would otherwise go on adding a correction the machine no longer needs.
//
// A hidden tab's timers are throttled to a crawl, so coming back into view is a look of its own —
// and the likeliest moment to find the clock moved, after a laptop has slept.
function keepWatch() {
    const needed = table !== null || correction !== 0;

    if (needed && vigil === null) {
        vigil = setInterval(check, clockWatch);

        if (typeof document !== 'undefined') {
            roused = () => {
                if (document.visibilityState === 'visible') {
                    check();
                }
            };

            document.addEventListener('visibilitychange', roused);
        }
    } else if (!needed && vigil !== null) {
        clearInterval(vigil);
        vigil = null;

        if (roused) {
            document.removeEventListener('visibilitychange', roused);
            roused = null;
        }
    }
}

/// Decides whether the page runs on the machine's clock or the site's, and by how much.
//
// Trystero reads `Date.now()` — never `new Date()` — for every Nostr timestamp it signs and every
// `since` it subscribes with. Correcting `Date.now` fixes both at the root, and leaves the bundle
// verbatim. Only `Date.now` is replaced: anything else on the page that asks it gets the corrected
// time too, which is the right time, and `new Date()` still says what the machine says.
//
// A clock ten seconds or more out is corrected by the whole of the offset; anything less is left
// alone, being well inside what every relay accepts. Once corrected, a page stays corrected until it
// is back within eight seconds, and a new measurement moves the correction only when it is two or
// more seconds from it, so a clock sitting on the line, or a header that truncates the other way,
// does not move the page's clock back and forth for nothing.
//
// The correction follows the machine's clock rather than standing for the page's life. Trystero
// also schedules by differences of `Date.now()` — a relay's backoff is stored as the moment it may
// next be tried, and a transfer times out by how long since it was last heard from — so moving the
// page's clock moves those timers by as much. 0.4.0 took that to mean the clock could only be
// corrected before Trystero first ran, and never touched again. But the machine's clock moves under
// those timers regardless: syncing it, or setting it by hand, jumps `Date.now` in a page that was
// never corrected at all, and in a page that was, a correction left standing turns the step into a
// page that is wrong by the whole of it for good. So a step is seen within a couple of seconds, the
// time is asked for again, and the correction moves to match. The page's clock is out for about as
// long as that takes and then right again, which also puts Trystero's timers back about where they
// were before the step. A measurement that arrives after a join has stopped waiting for it is put
// into effect for the same reason: the timers it moves were set on a clock the relays were refusing.
function settle(offset) {
    const tolerance = correction === 0 ? clockTolerance : clockTolerance - clockHysteresis;
    const wanted = Math.abs(offset) >= tolerance ? offset : 0;

    if (Math.abs(wanted - correction) < clockHysteresis || (wanted !== 0 && !install())) {
        return;
    }

    correction = wanted;
}

/// Replaces `Date.now` with one that adds the correction, the first time there is one to add.
//
// Once replaced it stays replaced, adding nothing when there is nothing to add. Putting the original
// back would gain nothing, and would undo anything else on the page that had wrapped it since.
function install() {
    if (!installed) {
        try {
            const corrected = () => realNow() + correction;
            Date.now = corrected;
            installed = Date.now === corrected;
        } catch {
            // A page that has frozen its built-ins. It keeps its own clock, and says so.
        }
    }

    return installed;
}

/// Tells C# how far out the machine's clock was last found to be, and whether the page is allowing
/// for it. Said at every join, and again after every measurement while at a table.
function tell(current) {
    if (measured !== null) {
        call(current, 'ReceiveClock', measured, correction !== 0);
    }
}

/// Sends on an action, to one peer or to everyone, without ever letting the failure escape.
//
// Sending is asynchronous and rejects when a peer disappears mid-send, which is ordinary rather
// than exceptional at a table people wander in and out of. Swallowing it here keeps one departure
// from surfacing as an unhandled rejection in everybody else's console.
function post(action, payload, peer) {
    try {
        const sent = action.send(payload, peer ? { target: peer } : {});

        if (sent && typeof sent.catch === 'function') {
            sent.catch(() => { });
        }

        return true;
    } catch {
        return false;
    }
}

/// Pulls the sender out of the metadata a message arrives with.
function who(from) {
    return from && typeof from.peerId === 'string' && from.peerId.length > 0 ? from.peerId : null;
}

/// A token bucket per peer per kind of message.
//
// Counted on the steady clock. On `Date.now` a window opened just before the clock moved back would
// not close until the clock caught up with it again, and a peer could be shut out for minutes.
function allow(current, peer, kind, allowance) {
    const now = steadyNow();
    const key = peer + '\u0000' + kind;
    const seen = current.allowances.get(key);

    if (!seen || now - seen.since > allowanceWindow) {
        current.allowances.set(key, { since: now, count: 1 });
        return true;
    }

    if (seen.count >= allowance) {
        return false;
    }

    seen.count++;
    return true;
}

/// Accepts a string of a believable length, and nothing else.
function text(data, maximum) {
    return typeof data === 'string' && data.length > 0 && data.length <= maximum ? data : null;
}

function call(current, method, ...args) {
    if (current.closed) {
        return;
    }

    try {
        const called = current.handler.invokeMethodAsync(method, ...args);

        if (called && typeof called.catch === 'function') {
            called.catch(() => { });
        }
    } catch {
        // The page navigated away mid-message. The room is about to be left anyway.
    }
}
