# Turn Chime

A small BepInEx 5 mod for **Across the Obelisk** that plays a sound when the game needs you, so you can look away from the window (or alt-tab) while waiting for friends in co-op.

**Status:** tested in single player on 2026-10-20: the sound plays on hero turns (also with the game in the background), on the card rewards screen and on the map. Co-op has not been tested yet.

## When it chimes

- **Your hero's turn** in combat. In co-op only for heroes you own; in single player for every hero.
- **Card rewards** after a combat.
- **Map choice**, when the map is ready for you to pick the next node.

It plays the game's own "it's your turn" sound by default, through its own audio source, so the game's "mute in background" option does not silence it. In co-op the game already plays that sound on your turn while the window is focused; the mod does not play it a second time then.

The mod changes nothing about gameplay. Only the players who want the chime need it; it does not need to match between co-op players.

## Settings (`BepInEx/config/com.cojoco.turnchime.cfg`)

| Section / key | Default | Meaning |
|---|---|---|
| `[Events] HeroTurn` | `true` | Chime at the start of your hero's turn |
| `[Events] CardRewards` | `true` | Chime when the card rewards screen opens |
| `[Events] MapChoice` | `true` | Chime when the map is ready for the next node |
| `[Sound] OnlyWhenUnfocused` | `false` | Only chime while the game window is not focused |
| `[Sound] Volume` | `1` | Chime volume, 0 to 1 |
| `[Sound] SoundFile` | empty | A `.wav` or `.ogg` file to play instead of the game's sound. A relative path is looked up in `BepInEx/config/` |

Restart the game after changing them. If the game itself pauses while unfocused, nothing can play until you come back; the mod does not change that.

## Logs

`BepInEx/LogOutput.log` gets `Turn Chime 0.1.0 loaded.` at startup and one `Chime: <reason>` line per sound (for example `Chime: Gustav's turn`).

## Install

1. Across the Obelisk with **BepInEx 5.4.x** (Windows: the `win_x64` build).
2. Copy `TurnChime.dll` into `BepInEx/plugins/`.
3. Start the game. The settings file is created on first launch.

## How it works (for modders)

| Patch | What |
|---|---|
| `MatchManager.SetActiveCharacter` Postfix | `CurrentHero` is set only on hero turns; `IsYourTurn()` checks co-op ownership |
| `RewardsManager.ShowRewards` Postfix | Card rewards (also `RewardsManagerMultiple`) |
| `MapManager.DoMapTips` Postfix | Only called from `BeginMapContinueEnd`, where the map becomes clickable in single player and co-op |

## Building

Needs the .NET SDK. The project targets `netstandard2.1`.

```
dotnet build -c Release
```

It references the game's DLLs and BepInEx/Harmony from the game folder, and copies the result into `BepInEx/plugins/`. The game folder defaults to the Linux Steam path; override it with `-p:GameDir="C:\path\to\Across the Obelisk"`. No game code is included in this project.
