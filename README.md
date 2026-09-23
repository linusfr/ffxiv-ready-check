# Ready Check

[![latest](https://img.shields.io/github/v/release/linusfr/ffxiv-ready-check?sort=semver&display_name=tag&label=latest&color=blue&cacheSeconds=300)](https://github.com/linusfr/ffxiv-ready-check/releases/latest)
[![ci](https://img.shields.io/github/actions/workflow/status/linusfr/ffxiv-ready-check/ci.yml?branch=main&label=ci&cacheSeconds=300)](https://github.com/linusfr/ffxiv-ready-check/actions/workflows/ci.yml)
[![licence](https://img.shields.io/github/license/linusfr/ffxiv-ready-check?color=blue)](LICENSE)

> Says what you're about to queue for, so nobody has to ask.

A ready check tells the party someone is asking. It does not tell them what for —
which is why every ready check is followed by "wait, which one?". This plugin
puts the answer in party chat a beat before the prompt arrives:

```text
[Party] Ready check: The Aurum Vale
```

Then the game's own ready check happens, exactly as it always did.

There is no second ready check here, no window, no button. You press the same
Ready Check you always pressed.

## Install

### Dalamud repo (recommended — auto-updates)

`/xlsettings` → **Experimental** → Custom Plugin Repositories → paste, `+`, then
click the **save** icon:

```
https://raw.githubusercontent.com/linusfr/ffxiv-ready-check/main/pluginmaster.json
```

Then `/xlplugins` → search **Ready Check** → Install.

### Direct download

| | |
|---|---|
| Latest | [`ReadyCheck.zip`](https://github.com/linusfr/ffxiv-ready-check/releases/latest/download/ReadyCheck.zip) |
| All releases | [releases](https://github.com/linusfr/ffxiv-ready-check/releases) |

Tags are unprefixed, so to pin a version substitute it directly:

```
https://github.com/linusfr/ffxiv-ready-check/releases/download/1.0.0/ReadyCheck.zip
```

Extract into `~/.xlcore/devPlugins/ReadyCheck/` (Linux) or
`%AppData%\XIVLauncher\devPlugins\ReadyCheck\` (Windows) and reload dev plugins.
A manually installed zip never updates itself; prefer the repo above.

## Example output

| Duty Finder state | Sent to party chat |
|---|---|
| One duty ticked | `Ready check: The Aurum Vale (Lv. 47)` |
| A duty with an item level requirement | `Ready check: The Great Gubal Library (Lv. 59, i120)` |
| A roulette with an Adventurer in Need | `Ready check: Leveling (Lv. 16, healer in need)` |
| Three duties ticked | `Ready check: 3 duties selected` |
| Three ticked, *List every selected duty* on | `Ready check: The Aurum Vale, Haukke Manor, Brayflox's Longstop` |
| Nothing selected | *nothing* |

Level, item level and Adventurer in Need are appended only for a **single**
selection — hanging a level on each of eight names, or on a bare count, is noise.
Item level is omitted where the game states none, which is most content below
level 50. Adventurer in Need is a roulette concept; Frontline, Crystalline
Conflict and the chocobo races have none, and nothing is invented for them.

Duty names are read out of the game in your client's language — nothing is
hardcoded — so a German or French client posts that client's duty names. Only the
default message text around them is English, and that is editable.

## Configuration

`/readycheckcfg`, or the cog next to the plugin in `/xlplugins`.

| Setting | Default | |
|---|---|---|
| Enable plugin | on | Off leaves the game's ready check completely untouched. |
| Announce in party chat | on | |
| Message format | `Ready check: {duty}` | `{duty}` becomes the name, the list, or the count. |
| List every selected duty | off | On names them all; off counts them. |
| Announce roulettes | on | Off stays quiet when the selection is a roulette. |
| Level and item level | on | `(Lv. 47)`, `(Lv. 59, i120)`. Single selection only. |
| Adventurer in Need | on | `(Lv. 16, healer in need)`. Roulettes only. |
| Announce anyway | off | Send a fallback line when the duty can't be determined. |
| Log what was detected and sent | off | Writes to `/xllog`. |

**Dry run** (button, or `/readycheckcfg test`) prints what a ready check would
send, to your own log only — see [Testing](#testing).

The default when the duty cannot be determined is **silence**. A ready check with
no message costs nothing; one naming the wrong duty costs a wipe.

## How it works

The interesting question is how to catch a ready check *before* it happens, since
the obvious hooks all fire after.

**Detecting the check.** Dalamud has no ready-check event.
`AgentReadyCheck.ReadyCheckEntries` only fills in once the check is already
running, and it fills in for checks *other people* started too — so it is both
too late and too broad. Instead this hooks
`AgentReadyCheck.InitiateReadyCheck`, the single client-side function every route
into a ready check goes through: the party list's button, the Duty Finder's, and
`/readycheck`. Hooking one function covers all three, and because it only runs on
the client that pressed the button, there is no way to announce somebody else's
check by accident. No screen coordinates, no simulated clicks, no polling.

**Reading the selection.** `AgentContentsFinder.SelectedContent` is a vector of
`ContentsId`, one per ticked entry. Each carries its own type — `Regular` (a
`ContentFinderCondition`) or `Roulette` (a `ContentRoulette`) — so roulettes,
single duties and multi-selection are all the same list read the same way, and
"which of these did they mean" never arises: with more than one, the plugin
counts rather than guesses. If nothing is ticked but the party is already in
queue, it falls back to `ContentsFinder.QueueInfo.QueuedEntries`.

**Adventurer in Need.** The server sends 11 role-bonus bytes on zone init and the
agent mirrors them as `ContentRouletteRoleBonuses`. The index is not the roulette
id: `ContentRouletteRoleBonus` is an 11-row sheet, and each roulette's row
reference into it is its slot — Leveling→1, Expert→5, Normal Raids→10, and so on,
checked against the game data rather than guessed. Row **0** is a sentinel meaning
*no such thing* rather than a real slot, which is what the roulettes without an
Adventurer in Need all point at. Every reference is 0–10, so the index cannot run
off the end of the array.

**Levels.** Read from the Excel sheets (`ClassJobLevelRequired`,
`ItemLevelRequired`) rather than from the agent, even though the agent has them.
The sheets carry the same numbers and cost no struct offsets that a patch could
move.

**Naming it.** Names are read from the agent's own `ContentList` — literally the
`Utf8String`s the Duty Finder is drawing — so they arrive already localised and
already capitalised. The Excel sheets are only a fallback for when that list is
gone.

Those names are SeStrings, not plain text, and they are read as such. Seven duties
encode a hyphen as a macro chunk rather than a literal `-`: *the Thousand Maws of
Toto-Rak* stores `02 1F 01 03` between "Toto" and "Rak". Decoding the buffer
directly turns that into four control characters, which the chat sanitiser then
strips — sending "TotoRak". `ExtractText` resolves the macro the way the game's own
renderer does. Tam-Tara, the Whorleater and the Merchant's Tale are the others.

**Ordering.** The detour sends the message, then calls the original. Both are
outbound packets on the same frame, so doing it in that order is what puts the
line above the ready-check prompt rather than below it. The original is called
from a `finally`: whatever this plugin gets wrong, the ready check still happens.

## Testing

A ready check needs a party, and the game will not start one without it — so the
hook firing and the send itself cannot be exercised alone. Everything before that
can:

```text
/readycheckcfg test
```

or the **Dry run** button in settings. It prints to your own log only:

```text
[Ready Check] Hook installed and enabled at 0x7FF64A1B2C30.
[Ready Check] Duty Finder: 1 selected.
[Ready Check]   Leveling — roulette=True, Lv.16, i0, inNeed=Healer
[Ready Check] Would send: /p Ready check: Leveling (Lv. 16, healer in need)
[Ready Check] You are not in a party, so a real ready check would stay silent.
```

That confirms the hook attached at a resolved address, that the selection is
being read, and the exact line that would go out — including the `/p` prefix and
the sanitising. Tick duties in the Duty Finder and re-run to check the
multi-select and roulette cases.

The per-entry line is the one to compare against the Duty Finder itself: the
level and the Adventurer in Need role should match the icons the game is drawing
on that row. Adventurer in Need moves over the day, so a mismatch means the
mapping is wrong, not that the value is stale.

What it cannot confirm: that `InitiateReadyCheck` actually fires, and that
`ProcessChatBoxEntry` delivers. Both need a second player.

## Known limitations

- **Dalamud has no chat-sending API.** `IChatGui.Print` writes only to your own
  log; nobody else sees it. The only route to the party is
  `UIModule.ProcessChatBoxEntry` — the function the chat box calls on Enter —
  with the text prefixed `/p`. That prefix is the entire guarantee of which
  channel this lands in, which is why the message is stripped of line breaks and
  leading slashes before sending. It cannot reach Say, Alliance or FC.
- **The Duty Finder has to have been opened this session** for names to resolve
  from the agent's list. It normally has been, by the act of selecting a duty.
- **Unsynced / preset parties and Party Finder listings** are not Duty Finder
  selections and have nothing to read; the plugin stays quiet.
- **Only your own ready checks** are announced. A check someone else starts never
  calls the hooked function.
- Struct offsets come from FFXIVClientStructs. A game patch can move them, so a
  patch day may need a Dalamud update before this works again — the usual deal.

**It does not answer ready checks.** There is no auto-ready, no auto-decline, no
timer, no tracking of who responded. It announces the duty and gets out of the
way.

## Development

```bash
just build      # debug build
just install    # build and drop into devPlugins
just check      # pre-commit hooks
just fmt        # dotnet format (style + analyzers)
just hooks      # install git hooks, once
```

Hermit pins prek/gitleaks/jq. The .NET SDK comes from nixpkgs via the justfile,
so no system install is needed. CI builds on `windows-latest` because Dalamud
needs the Windows targeting pack.

`ReadyCheck.json`, the plugin manifest, is generated by DalamudPackager from the
`<Name>`/`<Description>`/`<Tags>` properties in `src/ReadyCheck.csproj` — edit it
there, not as a checked-in file.

## Versioning

`go-semantic-release` reads conventional commits on `main`: `fix:` → patch,
`feat:` → minor, `feat!:` → major. It tags, releases, and CI attaches the zip and
updates `pluginmaster.json`. commitizen gates commit messages as a hook.

## Licence

MIT — see [`LICENSE`](LICENSE).
