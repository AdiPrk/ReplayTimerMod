# ReplayTimerMod

A BepInEx mod for Hollow Knight and Silksong that records your best time through each room and allows replaying it as a ghost.

## Features

- Records Hornet's position and animation at 30fps per room, up to 180s.
- Displays a ghost of your best run on subsequent attempts
- Import/export replays and collections via clipboard or file
- UI for managing replays and settings

Todo:
- Triggers for starting/stopping/recording replays (like pink dot)

## UI

Open the panel in game by pausing and clicking the **Replays** button in the bottom-left corner.

## Installation

Use a mod manager such as Cogfly.

For manual installation, drop the `ReplayTimerMod` folder from [releases](https://github.com/adiprk/replaytimermod/releases) into `BepInEx/plugins/`

## Build for Hollow Knight

This repository includes two Hollow Knight build targets:

- Projects: `src/HollowKnight/ReplayTimerMod.HK.1578.csproj` and
  `src/HollowKnight/ReplayTimerMod.HK.1221.csproj`
- Local path config: `.config/local/HollowKnightPath1578.props` /
  `.config/local/HollowKnightPath1221.props`

Steps for building 1221 (for 1578, replace all 1221 occurrences with 1578):

1. Edit `.config/local/HollowKnightPath1221.props` to point at your HK install and `hollow_knight_Data/Managed` folder.
2. Build with: `dotnet build src/HollowKnight/ReplayTimerMod.HK.1221.csproj -c Release`

To install manually in HK/Lumafly, extract/copy `ReplayTimerMod.HK.dll` into `hollow_knight_Data/Managed/Mods/ReplayTimerMod`.
