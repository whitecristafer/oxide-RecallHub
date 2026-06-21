# RecallHub

[English](README.md) | [Русский](Readme.ru.md)

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](#)
[![Status](https://img.shields.io/badge/status-stable-green.svg)](#)
[![Rust](https://img.shields.io/badge/game-Rust-orange.svg)](#)
[![Oxide](https://img.shields.io/badge/framework-Oxide%20%2F%20uMod-yellow.svg)](#)
[![Language](https://img.shields.io/badge/language-C%23-239120.svg)](#)

RecallHub is a Rust plugin for Oxide/uMod that provides controlled teleportation to key monuments such as Outpost and Bandit Camp, with configurable spawn points, cooldowns, countdowns, damage cancellation, and automatic update checks from a GitHub repository.

## Overview

RecallHub is designed to give players a clean and predictable teleport experience while keeping server-side control in the hands of administrators.

It is useful for servers that want:

- fast and reliable access to Outpost and Bandit Camp
- configurable permissions and cooldown rules
- teleport cancellation on damage
- support for custom or auto-detected spawn points
- version checks on server startup
- a simple and maintainable command structure

## Features

- Teleport to Outpost and Bandit Camp
- Custom teleport spawn points
- Auto-detection of monument spawn locations
- Separate permissions for each teleport type
- Optional no-cooldown permission
- Countdown before teleport execution
- Teleport cancellation by damage, fall damage, or player damage
- Optional blocking while mounted
- Optional blocking from Cargo Ship
- Hostile timer reset support
- Localization support for English and Russian
- Automatic update check against the latest GitHub release/source version
- Dev build protection: versions marked as development builds are ignored by the updater

## Commands

| Command | Description |
| --- | --- |
| `/otp` | Start teleport to Outpost |
| `/btp` | Start teleport to Bandit Camp |
| `/ttc` | Cancel active teleport |

Command names can be changed in the configuration.

## Permissions

| Permission | Description |
| --- | --- |
| `recallhub.outpost` | Allows use of Outpost teleport |
| `recallhub.bandit` | Allows use of Bandit Camp teleport |
| `recallhub.nocooldown` | Bypasses teleport cooldowns |

## Configuration

RecallHub creates its config automatically on first startup.

Main settings include:

- teleport blocking while mounted
- teleport blocking from Cargo Ship
- damage-based teleport cancellation
- hostile timer reset
- auto-detection of Outpost and Bandit Camp spawn points
- teleport offset
- cooldown values
- countdown values
- command names

Example configuration layout:

```json
{
  "BlockTeleportWhenMounted": false,
  "BlockTeleportFromCargo": false,
  "CancelTpAnyDamage": true,
  "CancelTpPlayerDamage": true,
  "CancelTpFallDamage": true,
  "ForceResetHostileTimer": true,
  "UseAutoDetectOutpost": true,
  "UseAutoDetectBandit": true,
  "TeleportOffsetY": 1.0,
  "OutpostCooldown": 30,
  "OutpostCountdown": 30,
  "BanditCooldown": 30,
  "BanditCountdown": 30,
  "OutpostCommand": "otp",
  "BanditCommand": "btp",
  "CancelCommand": "ttc"
}
```

## How it works

When a player uses a teleport command, the plugin:

1. validates permissions
2. checks whether teleportation is allowed
3. starts a countdown
4. cancels teleportation if the player takes damage, depending on config
5. selects a valid spawn point
6. teleports the player
7. applies cooldown if required

## Update system

RecallHub checks GitHub for the latest available version on startup.

Rules used by the updater:

- `1.0.0` is treated as a normal stable release version
- stable versions update only when the GitHub latest version is newer
- versions marked as development builds, such as `d1.0.1`, are ignored by the updater
- if the local version is newer than the published latest version, no downgrade is performed

This keeps production servers on stable builds only.

## Installation

1. Place `RecallHub.cs` into your `oxide/plugins` folder.
2. Restart the server or reload the plugin.
3. Verify permissions and config generation.
4. Adjust configuration if needed.

## Localization

The plugin includes built-in localization support for:

- English
- Russian

## Logging

RecallHub writes clear startup and update information into the server console for easier administration and maintenance.

## Requirements

- Rust dedicated server
- Oxide/uMod
- C# plugin support

## Notes

- This plugin is intended for server administration and gameplay convenience.
- Spawn detection may depend on the map layout and monument structure.
- Manual spawn configuration is available if auto-detection is not sufficient.

## License

Choose a license that matches your project policy before public release.
