# Changelog

What changed in each release, and what a consumer has to do about it. The tag is the version; see
[Releasing](README.md#releasing).

## 0.4.1

A fix to 0.4.0's clock correction, which was measured once and then kept for the page's life. A
player who synced their clock with the page open was left signalling on the old correction: their
page ran fast or slow by the whole of it, and the notice still said their clock was out.

No consumer change is needed. The app id, the relays, Inkwell's messages (`RollMessage`, `Hail`),
the table code alphabet and the vendored Trystero 0.26.0 are all unchanged; 0.4.0 and 0.4.1 sit at
the same tables.

### Fixed

- **The clock correction follows the machine's clock.** The transport watches the machine's clock
  against the browser's monotonic one, every two seconds and whenever the page comes back into
  view, and measures again within seconds of the clock being moved — synced, set by hand or woken
  from sleep. A clock set right drops the correction; a clock set wrong mid-session is corrected.
  It also measures again every ten minutes at a table, for drift. A move is never taken on trust:
  it always means a fresh measurement, and a measurement that fails keeps the correction in place
  and is retried with backoff. The backoff only spaces out the same question: a clock that moves
  again after a failure is asked about at once, then backed off from the start.
- **Leaving and rejoining measures again** when the last measurement is more than ten minutes old or
  the machine's clock has moved since, rather than reusing the page's first answer.
- **A late measurement is applied.** One that takes longer than the three seconds a join waits used
  to be reported and ignored; it is put into effect when it arrives.
- **The page's own rate limit no longer reads the corrected clock**, so a correction coming or going
  cannot hold back or wave through a peer's messages.

### Changed

- `PartyChannel.ClockOffset` is the latest measurement, not the first, and changes during a session.
- `PartyChannel.ClockCorrected` can go from `true` to `false` (the clock was set right) and from
  `false` to `true` (it was set wrong, or a late measurement arrived) while at a table. A correction
  stands until the clock is back within eight seconds, so one on the ten-second line does not flap.
  A large `ClockOffset` with this `false` now only happens on a page that cannot replace `Date.now`.
- `ReceiveClock` is called after every measurement while at a table, not only at a join. Both
  properties raise `Changed` when they change, as before, so a clock notice clears itself.
- `JoinAsync` waits for a measurement only when one is due, and not merely on the page's first join.

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
