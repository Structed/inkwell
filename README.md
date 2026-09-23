# Inkwell

A seeded generator engine for table-driven tabletop tools, in .NET.

Inkwell is the machinery underneath a generator — the randomness, the data loading, the drawing,
the sharing — with none of the game. It does not know what a settlement is, what a mouse is, or
which edition you are playing. You bring the tables; it makes rolling them reproducible, re-rollable,
translatable and shareable.

| Package | |
| --- | --- |
| `Structed.Inkwell` | The engine. |
| `Structed.Inkwell.Party.Blazor` | Optional. The browser half of the shared dice table. |
| `Structed.Inkwell.FantasiaArchive` | Optional. Writes and reads [Fantasia Archive](https://github.com/vishiri/fantasia-archive) projects. |

```pwsh
dotnet add package Structed.Inkwell
```

## What it does

**Randomness you can put in a URL.** PCG32 rather than `System.Random`, whose seeded output is not
stable across .NET versions — using it means an SDK upgrade silently invalidates every link anyone
ever shared. `Pcg32` is pinned against the reference vectors.

**A separate stream per field.** Each field draws from `SplitMix64(rootSeed ^ FNV1a(fieldPath))`, so
one value can be re-rolled without shifting any other. That is what makes "lock this, re-roll the
rest" possible at all: without it, changing the innkeeper's name changes everything after her.

**Locks that survive a language change.** A pin stores a table *position*, not the words on the
page. Pin a row in German, switch to English, and you get the English wording of the same row rather
than one stranded German line.

**Translation as an overlay.** A locale's JSON is deep-merged onto the canonical file before it is
deserialised, so the loader, the validator and every generator go on seeing a single well-formed
data file and never learn that languages exist.

**Provenance that travels with the data.** A data file records where it came from and under what
licence, including whether it is derived and what notices a site has to show — so attribution is a
property of the data rather than something a UI is trusted to remember.

**Maps, drawn rather than plotted.** Roads are grown first and buildings placed along them, inside
a silhouette you name, rendered as hand-inked SVG through a wobble pen that is itself seeded — so
the same seed produces the same drawing, down to the last shaky line.

**A versioned envelope for what came out.** Format id, version and payload, with the format and
version checked on the way back in, so a file from another tool is refused plainly instead of
half-read.

**A dice table the whole party can see.** Notation a person types — `4d6kh3+1` — rolled off a seed
so the result can be pasted into a chat window and rebuilt exactly by somebody whose browser could
not reach the table. Rolls travel directly between browsers over a table code, with no server and
no account; a private roll is reduced to the fact that somebody rolled before anything leaves the
machine, and the protocol is two message types wide so a channel that carries dice and names cannot
be talked into carrying anything else.

## A worked shape

A generator derives its own plan from `RollPlan`, so its `with` helpers return its own type while
the pin and re-roll bookkeeping stays shared:

```csharp
public sealed record KeepPlan : RollPlan
{
    public int Size { get; init; } = 3;
}
```

Then every value is addressed by a path, and each path gets its own stream:

```csharp
KeepPlan plan = new() { Seed = 0x0C179000, Size = 6 };
RollContext roll = new(plan);

// Honours a pin on this path, and records the row it landed on so locking it
// stores the position rather than the words.
string ruler = roll.Text("keep/ruler", rulers);
string mood = roll.Text("keep/mood", moods);

// Dice notation comes out of your data file rather than out of the code.
int garrison = DiceExpression.Roll(roll.Dice("keep/garrison"), "2d6+3");
```

The map is seeded from the plan rather than from the raw seed, so it can be redrawn on its own
without re-rolling the keep. Its silhouettes are deliberately abstract — `linear`, `vessel`, `boxy`,
`warren`, `sprawl` — because the engine has no opinion about whether a `vessel` is a boot, a teapot
or a beached hull. That mapping is your game's:

```csharp
uint mapSeed = MapGenerator.SeedFor(plan);

PlaceMap map = MapGenerator.Generate(new MapBrief
{
    Shape = "boxy",              // your ruined keep; somebody else's shipping container
    Scale = plan.Size,
    Subject = "Blackwater Hold",
    HasWater = true,
    Keys = [new MapKeySubject("The Broken Sword", "tavern")],
}, mapSeed);

string svg = SvgMapRenderer.Render(map, mapSeed, new MapRenderOptions
{
    AriaLabel = "A map of Blackwater Hold",
    CssClass = "keep-map",
});
```

Nothing above mentions a game. Swap the tables and the silhouette and the same engine draws a
different one.

## A field path is a load-bearing string

The single thing most worth knowing before building on this.

A field path such as `place/name` is an **input to the generator** — it is hashed into the seed of
that field's stream — *and* it is the **key a lock is filed under** in every link and every export
you have ever written. So renaming one does two things at once: it changes what a given seed rolls,
and it orphans every lock already saved against the old name.

Treat field paths, format ids, version numbers and exported JSON property names as a public
interface with users on the other side of it. Pin them in a test that compares whole generated
output against a committed fixture, and never regenerate that fixture to make a failing build pass
— that is the one move that defeats the entire safeguard.

## The shared dice table

`Structed.Inkwell.Party.Blazor` is a separate package because it is the only thing here that needs
a browser, and a tool that generates on a server or at a command line should not have to carry a
WebRTC transport to do it.

The rules live in `Structed.Inkwell` — the notation parser, the poker reading, the seeded roll, the
log, the roster, the table code and the two message types that are the entire protocol — and all of
it is testable without a page. This package is the other half: a `PartyChannel` that owns the
JavaScript module and decides what leaves the machine, shipped with that module beside it as a
static web asset.

```csharp
// The app id namespaces the signalling, so two unrelated tools sharing a public relay never meet.
// Everybody who is to sit at the same table must pass the same string, and changing it later
// strands every table code already written down — so pick one and keep it.
PartyChannel channel = new(js, "your-tool-dice") { Player = "Ada" };

await channel.JoinAsync(TableCode.Create());

DiceNotation.TryParse("4d6kh3+1", out DiceNotation notation);
await channel.RollAsync(DiceRolls.Roll(notation), reading: null, secret: false);
```

Two things are worth knowing before building on it. A private roll is **ghosted at the moment it is
made**, not at the moment it is sent, so there is never a full copy of it in a variable something
else might pick up; the dice are not encrypted or held back, they are never sent. And every message
that arrives is read by `RollMessage.TryRead` or `Hail.TryRead` before anything is done with it,
because on a public relay the input is whoever has the table code.

## Fantasia Archive

`Structed.Inkwell.FantasiaArchive` is a separate package because it is an integration with somebody
else's application rather than part of generating anything, and a tool that does not want it should
not have to carry it.

It writes and reads Fantasia Archive **v1** projects: the newline-delimited PouchDB dumps, the
polymorphic document and field model, paired relationships, reproducible document identities, and
the ZIP a page in a browser has to hand over in place of a folder. It also carries a hidden state
payload, so a generated thing can be exported, edited in someone else's app, and brought back still
re-rollable.

Fantasia Archive validates nothing on import — no version, no checksum, no schema — so the tests in
this repository stand in for the validator that does not exist. Two things in particular import
"successfully" and quietly produce a broken project: a document without a revision is dropped by the
loader, and a relationship written from only one end never gets the other side filled in.

Fantasia Archive is GPL-3.0. Only the identifiers needed to write a file it accepts are re-derived
here; no blueprint source, tooltip or value list is copied.

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```pwsh
dotnet test                  # the whole suite
dotnet pack -c Release       # every package
```

## Releasing

Releasing is tagging. `v0.2.1` builds, tests, packs and pushes `0.2.1`; the tag is the version, so
there is no second place to forget to update.

```pwsh
git tag v0.2.1 && git push origin v0.2.1
```

Publishing uses [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
nuget.org trades a signed GitHub OIDC token for an API key valid for one hour, so this repository
holds no publishing secret at all. The policy on nuget.org is pinned to these values, and changing
any of them in `publish.yml` means editing the policy to match:

| Policy field | Value |
| --- | --- |
| Repository Owner | `Structed` |
| Repository | `inkwell` |
| Workflow File | `publish.yml` |
| Environment | `nuget` |

The `user:` given to `NuGet/login` is the nuget.org **profile name** of the policy's creator —
`Structed.me` — not an email address and not the GitHub handle. They are easy to confuse: the bare
`structed` on nuget.org is a different account, and using it fails the token exchange with a 401
saying no matching trust policy was found.

## Where this came from

Inkwell was extracted from
[structed/mausritter-tools](https://github.com/structed/mausritter-tools), where it grew as the
guts of a Mausritter settlement generator before there was any reason to name it. It was lifted out
one layer at a time, each step checked against a golden baseline of four fully generated
settlements, so that a tool already in use by players kept producing byte-for-byte the same
settlements, maps, SVGs and exports throughout.

This repository starts from `8335d1597737d334a33e1372632efda0a50182bf` in that repository, which is
the commit the extraction finished at. The history before that point is over there.

## Licence

MIT. See [LICENSE](LICENSE).

Inkwell ships no game content: no tables, no names, no prose. What you roll on is yours, and its
licence is yours to get right.

### Trystero

The dice table's peer-to-peer connection uses [Trystero](https://github.com/dmotz/trystero) 0.25.4
by Dan Motzenbecker, MIT licensed. The Nostr strategy bundle is vendored verbatim at
`src/Inkwell.Party.Blazor/wwwroot/js/trystero-nostr.js`, with its origin and refresh instructions in
a banner at the top of the file.

Vendoring rather than loading it from a CDN keeps a site working when a CDN does not, and means no
third party can change what runs on the page between one session and the next. The bundle carries
its own copy of [@noble/secp256k1](https://github.com/paulmillr/noble-secp256k1) by Paul Miller,
also MIT licensed, which Trystero uses to sign the Nostr events that carry the signalling.

To refresh it, download
`https://esm.sh/trystero@0.25.4/es2022/trystero.bundle.mjs` — bumping the version in the URL and in
the banner together — replace everything below the banner, and check the bundle is still
self-contained, which means it declares no imports of its own. It is saved as `.js` rather than
`.mjs` because GitHub Pages serves `.mjs` with a MIME type browsers refuse to import, and it is
pinned to LF in `.gitattributes` so a refresh shows a diff of what actually changed rather than of
every line.

Neither library sees a roll. They establish the connection; the dice travel directly between
browsers over it.
