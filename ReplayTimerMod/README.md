# ReplayTimerMod

A BepInEx mod for Hollow Knight: Silksong that records your best time through each room and allows replaying it as a ghost.

## Features

- Records Hornet's position and animation at 30fps per room, up to 180s.
- Displays a ghost of your best run on subsequent attempts
- Import/export replays and collections via clipboard or file
- UI for managing replays and configuring the ghost

Website for improved sharing, visualizations, and more is still under works.

## Installation

Drop `ReplayTimerMod.dll` into `BepInEx/plugins/`

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