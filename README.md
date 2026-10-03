<div align="center">
	<img src="images/icon.png" alt="Ready Check icon" width="128">
	<h1>Ready Check</h1>
	<p>Says what you're about to queue for, so nobody has to ask.</p>
	<p>
		<a href="https://github.com/linusfr/ffxiv-ready-check/releases/latest"><img src="https://img.shields.io/github/v/release/linusfr/ffxiv-ready-check?sort=semver&amp;display_name=tag&amp;label=latest&amp;color=blue&amp;cacheSeconds=300" alt="Latest release"></a>
		<a href="https://github.com/linusfr/ffxiv-ready-check/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/linusfr/ffxiv-ready-check/ci.yml?branch=main&amp;label=ci&amp;cacheSeconds=300" alt="CI status"></a>
		<a href="LICENSE"><img src="https://img.shields.io/github/license/linusfr/ffxiv-ready-check?color=blue" alt="MIT license"></a>
	</p>
</div>

A ready check tells the party someone is asking, not what for. This puts the
answer in party chat a beat before the prompt arrives:

```text
[Party] Ready check: Leveling (Lv. 16, healer in need)
```

Then the game's own ready check happens, unchanged. Same button, no extra window.
It only announces checks you initiate, stays silent inside duties, and never
answers a ready check for you.

## Output

| Duty Finder | Sent to party |
|---|---|
| One duty | `Ready check: The Aurum Vale (Lv. 47)` |
| With an item level requirement | `Ready check: The Great Gubal Library (Lv. 59, i120)` |
| Roulette with an Adventurer in Need | `Ready check: Leveling (Lv. 16, healer in need)` |
| Several duties | `Ready check: 3 duties selected` |
| With your job announced | `Ready check: Leveling (Lv. 16, healer in need) — on WHM (healer)` |
| Several, *List every selected duty* | `Ready check: The Aurum Vale, Haukke Manor, Sastasha` |
| Nothing selected | *nothing* |

Duty names come from the game in your client's language. Level and Adventurer in
Need are appended only for a single selection — on eight names, or on a count,
they are noise. Item level is omitted where the game states none (most content
below 50), and Adventurer in Need where the roulette has none (Frontline,
Crystalline Conflict, chocobo racing).

## Install

In `/xlsettings`, open **Experimental** > **Custom Plugin Repositories**, paste
this URL, click `+`, then save:

```
https://raw.githubusercontent.com/linusfr/ffxiv-ready-check/main/pluginmaster.json
```

Then open `/xlplugins`, search for **Ready Check**, and select **Install**. Or grab
[`ReadyCheck.zip`](https://github.com/linusfr/ffxiv-ready-check/releases/latest/download/ReadyCheck.zip)
and extract into `~/.xlcore/devPlugins/ReadyCheck/` (Linux) or
`%AppData%\XIVLauncher\devPlugins\ReadyCheck\` (Windows) — a manual zip never
self-updates, so prefer the repo.

## Configuration

`/readycheckcfg`, or the cog in `/xlplugins`.

| Setting | Default | |
|---|---|---|
| Enable plugin | on | Off leaves the game's ready check untouched. |
| Announce in party chat | on | |
| Message format | `Ready check: {duty}` | `{duty}` = name, list, or count. |
| List every selected duty | off | On names them all; off counts them. |
| Announce roulettes | on | |
| Level and item level | on | `(Lv. 47)`, `(Lv. 59, i120)` |
| Adventurer in Need | on | `(Lv. 16, healer in need)` |
| Announce anyway | off | Send a fallback when the duty is unknown. |
| Log what was detected and sent | off | Writes to `/xllog`. |

When the duty cannot be determined the default is **silence**. A ready check with
no message costs nothing; one naming the wrong duty costs a wipe.

## Testing

`/readycheckcfg test`, or the **Dry run** button, prints what a ready check would
send — to your own log only:

```text
[Ready Check] Hook installed and enabled at 0x7FF64A1B2C30.
[Ready Check] Duty Finder: 1 selected.
[Ready Check]   Leveling — roulette=True, Lv.16, i0, inNeed=Healer
[Ready Check] Would send: /p Ready check: Leveling (Lv. 16, healer in need)
```

The per-entry line is what you compare against the Duty Finder. A real ready
check needs a party, so the hook firing and the send itself cannot be exercised
alone; everything before them can.

## How it works

**Catching the check.** Dalamud has no ready-check event, so this hooks
`AgentReadyCheck.InitiateReadyCheck`, the client-side function reached by the
party list button, the Duty Finder button, and `/readycheck`. The game also calls
it when another player's check arrives. On that incoming path,
`ReadyCheckEntries` is already populated; Ready Check detects that state and stays
silent. Locally initiated checks reach the hook before those entries exist.

The detour sends, then calls the original. The original is called from a
`finally`, so whatever the plugin gets wrong, the ready check still happens.
Checks started inside a duty are deliberately left unannounced: the selected or
queued Duty Finder entry is stale context once the party is already inside.

**Reading the selection.** `AgentContentsFinder.SelectedContent` is a vector of
`ContentsId`, one per ticked entry, each typed `Regular` or `Roulette`. Single,
multiple and roulette selections are the same list read the same way. With more
than one, the plugin counts rather than guesses. Falls back to
`ContentsFinder.QueueInfo.QueuedEntries` when already queued.

**Adventurer in Need.** The server sends 11 role-bonus bytes on zone init. The
index is *not* the roulette id — `ContentRouletteRoleBonus` is an 11-row sheet,
and each roulette's reference into it is its slot (Leveling→1, Expert→5, Normal
Raids→10, …), checked against the game data. Row 0 is a sentinel meaning *no such
thing*, which is what the roulettes without one point at.

**Names.** Taken from the agent's own `ContentList` — the strings the Duty Finder
is drawing — so they are already localised and capitalised; Excel is the
fallback. They are SeStrings, not plain text: seven duties encode a hyphen as a
macro chunk (*the Thousand Maws of Toto-Rak* stores `02 1F 01 03`), which decoded
raw becomes control characters and gets stripped on the way to chat.
`ExtractText` resolves it as the game's renderer does.

**Levels** come from Excel, not the agent — same numbers, no struct offsets a
patch can move.

## Limitations

- **Dalamud has no chat-sending API.** `IChatGui.Print` is local-only. The only
  route out is `UIModule.ProcessChatBoxEntry` with a `/p` prefix — that prefix is
  the whole guarantee of channel, which is why the message is stripped of line
  breaks and leading slashes first. It cannot reach Say, Alliance or FC.
- **Only your own ready checks.** Incoming checks have populated ready-check
  entries and are ignored.
- Checks started after entering a duty are ignored.
- Party Finder listings and preset parties are not Duty Finder selections; the
  plugin stays quiet.
- Struct offsets come from FFXIVClientStructs, so a patch may need a Dalamud
  update first.

## Development

```bash
just build      # debug build
just install    # build and drop into devPlugins
just check      # pre-commit hooks
just fmt        # dotnet format (style + analyzers)
just hooks      # install git hooks, once
```

Hermit pins prek/gitleaks/jq; the .NET SDK comes from nixpkgs via the justfile.
CI builds on `windows-latest` because Dalamud needs the Windows targeting pack.
`ReadyCheck.json` is generated by DalamudPackager from the csproj properties —
edit it there, not as a file.

`go-semantic-release` reads conventional commits on `main`: `fix:` → patch,
`feat:` → minor, `feat!:` → major. The first release is always `1.0.0` whatever
the message says. CI tags, attaches the zip, and updates `pluginmaster.json`.

## License

MIT, see [`LICENSE`](LICENSE).
