# Changelog

What changed in each release, and what a consumer has to do about it. The tag is the version; see
[Releasing](README.md#releasing).

## 0.4.0

A reliability release for the shared dice table. Players who could not see each other — or who saw
somebody stuck saying hello, or a seat for somebody long gone — should now find each other, or at
least be told why not.

### Breaking

- **Trystero 0.26.0.** The vendored transport moves from Trystero 0.25.4 to 0.26.0, whose wire
  format between browsers changed. A player on an Inkwell before 0.4.0 and a player on 0.4.0 can
  find each other through the relays and then never exchange a roll. **Change your app id when you
  upgrade** (`your-tool-dice` → `your-tool-dice-2`, say), so old and new builds of your site sit at
  separate tables instead of half-connecting while browsers and caches catch up. Inkwell's own
  messages — `RollMessage` and `Hail` — are unchanged, byte for byte.

### Fixed

- **A wrong clock no longer makes a player invisible.** Nostr relays judge an event by the clock that
  signed it. Most refuse an ephemeral event more than a minute old
  (`invalid: ephemeral event expired`, or `invalid: created_at too early`); some refuse one far in
  the future (`invalid: created_at too late`), at a limit each relay sets for itself; and relays
  that take a skewed event anyway hide it behind other players' `since`, or hide everybody else's
  from a fast clock. Before it first joins a table, the transport now measures the machine's clock
  against the site's `Date` header and, if it is ten seconds or more out, signals on the site's time
  for the rest of the page's life.
- **Relays are not given up on.** Under 0.25.4 a relay that answered `invalid:` once — as most
  relays do to a slow clock — was dropped for the page's life, and a socket that kept
  failing was closed for good after about a minute of backoff. 0.26.0 retires a relay only for
  being blocked, restricted or asking for auth or proof of work, and retries the rest forever with
  backoff capped at a minute, re-announcing on reconnect.
- **Missed arrivals and departures repair themselves.** Every two seconds the seats are checked
  against the connections Trystero actually holds, so a departure it never announced no longer
  leaves a ghost seat, and an arrival it never announced is seated.
- **A lost hello is said again.** Each player says their name to the table every thirty seconds and
  whenever the page comes back into view, so a seat no longer stays at "still saying hello" because
  one message went missing.
- A peer that left and came back within ten seconds was still held to the message allowance it had
  used before it left, and the allowances of everybody who ever left were kept until the table was.
  Both are cleared on departure now.

### Added

- `PartyChannel.ClockOffset` (`TimeSpan?`): how far the machine's clock was found to be from the
  site's — positive when the machine is behind, `null` if it could not be measured.
- `PartyChannel.ClockCorrected` (`bool`): whether the page is signalling on the site's time. A
  measurement that takes longer than three seconds is reported but not applied, so a large
  `ClockOffset` with this `false` is a player who should reload or fix their clock.
- `PartyChannel.Unreachable` (`int`): how many peers found the table through the relays but could
  not be connected to directly — almost always two networks that need a TURN server — and have not
  connected, said hello or left since.
- `[JSInvokable]` `ReceiveClock(double, bool)`, `ReceiveJoinError(string?)` and
  `ReceiveArrival(string?)` on `PartyChannel`, called by the packaged transport. Each checks what
  JavaScript hands it, ignores it while away from a table, and raises `Changed` only when something
  changed.

### Notes

- Joining a table now waits up to three seconds, once per page, for the clock measurement. It is
  a `HEAD` request for the page itself and is usually answered in a fraction of that.
- `getRelaySockets` is still exported from
  `_content/Structed.Inkwell.Party.Blazor/js/trystero-nostr.js`.

## 0.3.0

- A host can name the Nostr relays its tables signal through, instead of Trystero's fixed draw
  (#3).

## 0.2.0

- The shared dice table: `Structed.Inkwell.Party.Blazor`, with the dice notation, poker reading,
  seeded rolls, log, roster, table codes and messages in `Structed.Inkwell` (#1).

## 0.1.1

- Rendering produces the same bytes wherever the engine was compiled.

## 0.1.0

- The engine, lifted out of a Mausritter settlement generator, published with trusted publishing.
