# Ironite Launcher

The Windows launcher for Ironite games. It works like a small game store: every
game lives in its own GitHub repository, the launcher installs the game from that
repository's latest release, and it keeps installed games (and itself) up to date.

**Download:** [latest release](../../releases/latest) → `IroniteLauncher-Setup-<version>.exe`.
Installed launchers update themselves.

## How it works

- **Library.** The game list is [`launcher/web/games.json`](launcher/web/games.json).
  Installed launchers also read the copy on `main` here, so a game added to that
  file shows up without a launcher update (cover art for a brand-new game still
  needs a launcher release; until then it gets a generic cover).
- **Installing.** For a game with a `repo`, the launcher reads
  `GET /repos/<repo>/releases/latest` and downloads the release's
  `*-windows.zip` (unpacked into `%LOCALAPPDATA%\Ironite\Launcher\Games\<bundle>\<version>`)
  or, if there's no zip, its `*Setup*.exe` (NSIS or Inno Setup, run silently into
  `Games\<bundle>\app`). Downloads are checked against GitHub's SHA-256 digests.
- **Updating games.** The launcher checks every repository when it starts and every
  30 minutes (cached with ETags, so it stays within GitHub's anonymous rate limit).
  A newer release of an installed game is downloaded and installed automatically,
  never while the game is running. Automatic updates can be turned off on the About page.
- **Coming soon.** A game without a `repo`, or whose repository has no published
  release yet, is shown as Coming Soon.
- **Demos.** `"demo": true` marks a game as a demo (Rewind Life): it gets a DEMO
  badge and "Install Demo" / "Play Demo" buttons.
- **Updating the launcher.** The launcher checks this repository's latest release. When
  it's newer, a banner offers to update; the launcher downloads
  `IroniteLauncher-Setup-<version>.exe`, runs it with `/S /relaunch` and exits; setup
  closes the old launcher, installs the new one and opens it again.

## Game repositories

| Game | Repository | Release file |
| --- | --- | --- |
| Klondike | [adamglassner/klondike](https://github.com/adamglassner/klondike) | `Klondike-<v>-windows.zip` |
| Texas Hold'em | [adamglassner/texasholdem](https://github.com/adamglassner/texasholdem) | `TexasHoldem-<v>-windows.zip` |
| Blackjack | [adamglassner/blackjack](https://github.com/adamglassner/blackjack) | `Blackjack-<v>-windows.zip` |
| Go Fish | [adamglassner/gofish](https://github.com/adamglassner/gofish) | `GoFish-<v>-windows.zip` |
| Old Maid | [adamglassner/oldmaid](https://github.com/adamglassner/oldmaid) | `OldMaid-<v>-windows.zip` |
| Euchre | [adamglassner/euchre](https://github.com/adamglassner/euchre) | `Euchre-<v>-windows.zip` |
| Maple Lane Finds | [adamglassner/maple-lane-finds](https://github.com/adamglassner/maple-lane-finds) | `MapleLaneFinds-Setup-<v>.exe` |
| Solar Siege | [adamglassner/Solar-Siege](https://github.com/adamglassner/Solar-Siege) | `SolarSiege-<v>-windows.zip` |
| Snugfit | [adamglassner/snugfit](https://github.com/adamglassner/snugfit) | `Snugfit-Setup-<v>.exe` |
| Chess | [adamglassner/chess](https://github.com/adamglassner/chess) | `Chess-<v>-windows.zip` |
| Checkers | [adamglassner/checkers](https://github.com/adamglassner/checkers) | `Checkers-<v>-windows.zip` |
| Rewind Life (demo) | [adamglassner/RewindLife](https://github.com/adamglassner/RewindLife) | `RewindLife-<v>-windows.zip` |

Each game repository has a Release workflow that publishes a new release when its
version changes, so releasing a game update is: bump its version, add a changelog
entry, push to `main`. (Maple Lane Finds keeps its own draft-then-publish flow.)
Repositories must be **public**: the launcher downloads without signing in.

To add a game: give it a repository with releases like the ones above, then add an
entry to `games.json` with `repo`, `bundle` (its folder name), `exe` and `category`.

## Building

`bash build.sh` (Go 1.24+ and NSIS 3) builds `dist/IroniteLauncher-Setup-<version>.exe`.
The NSIS plugins it uses (nsProcess, StdUtils) are in `installer/plugins`.

## Releasing the launcher

Bump `VERSION`, add a section at the top of `CHANGELOG.md`, push to `main`. The
[Release workflow](.github/workflows/release.yml) builds the installer and publishes
`v<version>`; installed launchers offer the update the next time they check.

## Testing locally

`IRONITE_DEV=1` serves the UI and prints its URL instead of opening a window.
`IRONITE_GITHUB_API=<url>` points the launcher at a mock GitHub API,
`IRONITE_CATALOG_URL=off` skips the published catalog, and `IRONITE_CACHE_DIR`
moves the games and download cache.

(c) 2026 Ironite. All rights reserved.
