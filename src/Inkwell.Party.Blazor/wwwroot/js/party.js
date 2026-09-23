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

let table = null;

/// Joins a table. Any table already open is left first, so there is only ever one.
//
// `appId` namespaces the signalling so two unrelated apps using the same relays never meet, even if
// somebody picks the same room code by accident. It is supplied by the caller rather than fixed
// here, because this file is shared and the app it is running inside is the only thing that knows
// which app it is.
export async function join(appId, code, handler) {
    leave();

    const room = joinRoom({ appId }, code);

    // Trystero hands back one object per action, and the receiving side is a settable property
    // rather than a register-a-callback function. The sender is `send(payload, { target })`, and
    // the handler is called with `(payload, { peerId })` — the peer arrives in a bag of metadata,
    // not as a bare second argument.
    const roll = room.makeAction('roll');
    const ask = room.makeAction('ask');
    const tale = room.makeAction('tale');
    const hail = room.makeAction('hail');

    const current = {
        room,
        handler,
        roll,
        ask,
        tale,
        hail,
        allowances: new Map(),
        asked: new Set(),
        closed: false
    };

    table = current;

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

        // Asked once per peer, by both sides. The newcomer gets the history they wanted; the
        // established player gets a batch they already have and discards it. Symmetry is cheaper
        // here than working out which of us arrived first.
        if (!current.asked.has(peer)) {
            current.asked.add(peer);
            post(current.ask, '', peer);
        }

        await hello(current, peer);
    };

    room.onPeerLeave = peer => {
        current.allowances.delete(peer);
        current.asked.delete(peer);
        call(current, 'ReceiveDeparture', peer);
        peers(current);
    };

    call(current, 'ReceiveStatus', 'joining');
    watch(current);

    return selfId;
}

/// Sends one roll to everybody at the table.
export function send(json) {
    if (!table || table.closed || typeof json !== 'string' || json.length > maximumRollBytes) {
        return false;
    }

    return post(table.roll, json);
}

/// Tells everybody at the table what this player is now called.
export function greet(json) {
    if (!table || table.closed || typeof json !== 'string' || json.length > maximumHailBytes) {
        return false;
    }

    return post(table.hail, json);
}

/// Leaves the table. Safe to call when there is no table, and safe to call twice.
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

    try {
        const left = closing.room.leave();

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
    };

    look();
    current.watcher = setInterval(look, 2000);
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

/// Introduces this player to one peer, in whatever words C# chooses.
//
// Both sides do this when they see each other, for the same reason both sides ask for the history:
// it costs one small message and saves working out which of the two arrived first.
async function hello(current, peer) {
    try {
        const greeting = await current.handler.invokeMethodAsync('Greeting');

        if (greeting && !current.closed) {
            post(current.hail, greeting, peer);
        }
    } catch {
        // The page has gone. There is nobody left to introduce.
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
