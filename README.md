# ReplayTimerMod

A BepInEx mod for Hollow Knight: Silksong that records your best time through each room and allows replaying it as a ghost.

## Features

- Records Hornet's position and animation at 30fps per room, up to 180s.
- Displays a ghost of your best run on subsequent attempts
- Import/export replays and collections via clipboard or file
- UI for managing replays and configuring the ghost

Todo:
- Website for improved sharing, visualizations, and more is still under works.
- Triggers for starting/stopping/recording replays (like pink dot)

## Installation

Drop the `ReplayTimerMod` folder from [releases](https://github.com/adiprk/replaytimermod/releases) into `BepInEx/plugins/`

## UI

Open the panel in game by pausing and clicking the menu icon (`≡`) in the bottom-left corner.

## Ghost behaviour

The ghost follows the route that matches your current entry and exit scene. If multiple routes exist for the same entry scene, the fastest one is shown.

## Config

Settings are saved to `BepInEx/config/io.github.adiprk.replaytimermod.cfg`

```ini
[Ghost]
Enabled = true
ColorR  = 1
ColorG  = 1
ColorB  = 1
Alpha   = 0.4
```

## Build for Hollow Knight

This repository now includes a Hollow Knight build target:

- Project: `src/HollowKnight/ReplayTimerMod.HK.csproj`
- Local path config: `.config/local/HollowKnightPath.props`

Steps:

1. Edit `.config/local/HollowKnightPath.props` to point at your HK install and `hollow_knight_Data/Managed` folder.
2. Build with:
   `dotnet build src/HollowKnight/ReplayTimerMod.HK.csproj -c Release`
3. Build output is written only inside this repo (no auto-copy into game folders):
   `obj/hk/Release/dist/ReplayTimerMod/ReplayTimerMod.HK.dll`
4. Release zip is generated automatically in Release builds:
   `obj/hk/Release/dist/ReplayTimerMod.HK-v<version>.zip`

To install manually in HK/Lumafly, extract/copy `ReplayTimerMod.HK.dll` into `hollow_knight_Data/Managed/Mods/ReplayTimerMod`.
