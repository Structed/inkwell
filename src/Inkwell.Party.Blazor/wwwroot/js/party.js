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

// How long a join waits to hear the time before going ahead without it.
const clockPatience = 3000;

// A fixed floor: the day this check was written. No server telling the truth can send a date before
// it, so a header that does is a broken proxy, and believing it would wind the page back years. It
// only ever has to be earlier than now, so it never needs moving when this file changes.
const earliestPlausibleDate = Date.UTC(2026, 9, 1);

// The machine's own clock, taken before anything here has had the chance to correct it.
const realNow = Date.now.bind(Date);

let table = null;

// The clock is measured once per page, and the decision about whether to correct it is taken once
// per page — before Trystero first runs, and never again. See `settle` for why it cannot be taken
// later, or undone.
let clock = null;
let settled = false;
let correction = null;

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

    const offset = await hear();

    if (current.closed) {
        return selfId;
    }

    settle(offset);

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

    report(current, offset);

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
// The clock correction, if there is one, is deliberately left in place. See `settle`.
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

/// Asks the site what time it is, once per page.
//
// The site is the one clock every player at a table has already agreed to trust, by loading it.
// A HEAD request for the page itself, with a query nothing else uses and `no-store`, goes past
// the browser's cache — and past the service worker Blazor's template installs, which only ever
// answers GETs — to a server that stamps a fresh `Date` on its answer. GitHub Pages does even when
// its CDN answers from cache, and so does Cloudflare Pages.
//
// The header is only good to the second and is truncated, not rounded, so the true time is half a
// second past it on average; the round trip is split down the middle for the same reason.
function measure() {
    return clock ??= (async () => {
        try {
            if (typeof document === 'undefined' || typeof fetch !== 'function') {
                return null;
            }

            const url = new URL(document.baseURI);
            url.hash = '';
            url.searchParams.set('inkwell-clock', realNow().toString(36) + Math.random().toString(36).slice(2));

            const before = realNow();
            const response = await fetch(url, { method: 'HEAD', cache: 'no-store' });
            const after = realNow();
            const said = Date.parse(response.headers.get('Date') ?? '');

            if (!Number.isFinite(said) || said < earliestPlausibleDate) {
                return null;
            }

            return Math.round(said + 500 - (before + after) / 2);
        } catch {
            // Offline, blocked, or a host that sends no Date. The table goes ahead on the machine's
            // own clock, which is right for nearly everybody.
            return null;
        }
    })();
}

/// Waits for the clock a little while, and no longer.
//
// Resolves to the offset in milliseconds, `null` when there is no telling, or `undefined` when the
// answer is still on its way — which is different, because it may yet be worth reporting.
async function hear() {
    let timer = null;

    try {
        return await Promise.race([
            measure(),
            new Promise(resolve => {
                timer = setTimeout(() => resolve(undefined), clockPatience);
            })
        ]);
    } finally {
        clearTimeout(timer);
    }
}

/// Decides, once per page, whether the page runs on its own clock or the site's.
//
// Trystero reads `Date.now()` — never `new Date()` — for every Nostr timestamp it signs and every
// `since` it subscribes with. Correcting `Date.now` before it first runs fixes both at the root, and
// leaves the bundle verbatim.
//
// It has to be before, and it has to be for good. Trystero also schedules by differences of
// `Date.now()` — a relay's backoff is stored as the moment it may next be tried — so moving the clock
// under a timer already set would stretch or shrink it by the whole offset. Correcting after the
// first join would do that to every timer it had set; undoing the correction on leaving would do it
// to every timer still running. So the first join decides, the decision stands until the page
// goes, and a measurement that arrives too late for that first join is reported but never applied.
//
// Only `Date.now` is replaced. Anything else on the page that asks it gets the corrected time too,
// which is the right time; `new Date()` still says what the machine says.
function settle(offset) {
    if (settled) {
        return;
    }

    settled = true;

    if (typeof offset !== 'number' || !Number.isFinite(offset) || Math.abs(offset) < clockTolerance) {
        return;
    }

    try {
        Date.now = () => realNow() + offset;
        correction = offset;
    } catch {
        // A page that has frozen its built-ins. It keeps its own clock, and says so.
    }
}

/// Tells C# how far out the machine's clock was found to be, and whether the page is running on it.
function report(current, offset) {
    const tell = measured => {
        if (typeof measured === 'number' && Number.isFinite(measured)) {
            call(current, 'ReceiveClock', measured, correction !== null);
        }
    };

    if (offset === undefined) {
        measure().then(tell, () => { });
    } else {
        tell(offset);
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
function allow(current, peer, kind, allowance) {
    const now = Date.now();
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
