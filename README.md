# Ironite Launcher

The launcher for Ironite games on Windows 10/11 (x64). It installs each game from its own GitHub
repository's releases, keeps them up to date, starts them and tracks play time, and updates itself from
this repository's releases.

Built the same way as Bytehawk Launcher: **C# / WPF on .NET 8**, published as a self-contained single-file
exe, and installed with **Inno Setup 6**.

## Features

- Library with a featured carousel, categories, search (Ctrl+F), favorites and hidden games
- Install, update (automatically, every 30 minutes) and uninstall games from GitHub Releases, one download at a time
- Play tracking: opens, play time, sessions (also for games started outside the launcher), weekly Stats page
- Non-Ironite games: add any .exe or Xbox / Microsoft Store game, with categories, details and cover art
- Achievements for Ironite games (read from the files the games write), plus Steam and Xbox (OpenXBL) achievements for your own games
- Desktop shortcuts that start a game through the launcher
- Launcher updates like Bytehawk Launcher: download the new setup, run it silently, reopen

## Data

| Where | What |
| --- | --- |
| `%APPDATA%\Ironite\Launcher\settings.json` | play stats, installs, favorites, hidden, your own games, accounts |
| `%APPDATA%\Ironite\Launcher\CustomIcons\` | icons and cover art for your own games |
| `%APPDATA%\Ironite\Launcher\WebGames\` | browser profiles (saves) for web games |
| `%LOCALAPPDATA%\Ironite\Launcher\Games\` | downloaded games |
| `%LOCALAPPDATA%\Ironite\Launcher\releases.json` | cached GitHub releases and game catalog |
| `%APPDATA%\Ironite\Achievements\<game id>.json` | each Ironite game's achievements, written by the game (icons in `<game id>\`) |
| `%TEMP%\IroniteLauncher.log` | log |

These are the same files launcher 2.x used, so updating from 2.x keeps everything.

## Game catalog

`catalog/games.json` lists the games (schema 2). Launchers read the latest copy from this repository, so new
games appear without a launcher update; covers for new games go in `covers/<id>.jpg` (480×600) and, for the
featured carousel, `covers/<id>-hero.jpg` (1600×540). `launcher/web/games.json` is a copy kept for 2.x launchers.

## Building

```powershell
.\build.ps1                  # version from src\IroniteLauncher\IroniteLauncher.csproj
.\build.ps1 -SkipInstaller   # exe only
```

Needs the .NET 8 SDK and Inno Setup 6. Output: `dist\IroniteLauncher.exe` and `dist\IroniteLauncher-Setup-<version>.exe`.

`IroniteLauncher.exe --self-test <file>` runs the headless checks, and `--tour <folder>` installs, plays and
uninstalls a real game and saves screenshots of every page.

## Releasing

Bump `<Version>` in the .csproj, add a `CHANGELOG.md` entry, and push to `main`. GitHub Actions builds,
tests and publishes the release (the Setup .exe is what installed launchers update from).

© 2026 Ironite. All rights reserved.
