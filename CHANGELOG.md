# Changelog

All notable changes to Ironite Launcher are recorded here.
Versions follow Semantic Versioning (MAJOR.MINOR.PATCH): MAJOR for big overhauls,
MINOR for new games and features, PATCH for fixes.

## [2.2.1] - 2026-10-31

### Fixed
- After the launcher updated itself, it could keep showing the game list from before the update, so newly added games (like Carnival Constructor) were missing until much later. It now shows the new list straight away.

## [2.2.0] - 2026-10-31

### Added
- Carnival Constructor, a traveling carnival sim, is now listed in Simulation as Coming Soon, with its own cover art.

## [2.1.0] - 2026-10-31

### Added
- Big Top Builder, a circus management sim, is now listed in Simulation as Coming Soon, with its own cover art.

## [2.0.2] - 2026-10-31

### Fixed
- Update checks no longer run out of GitHub's hourly allowance, which made the launcher say it was offline. The launcher now asks github.com which version is newest and only downloads release details when something has changed.
- The launcher closes properly when you close its window, so it opens again straight away instead of saying it's already running.
- Opening the launcher while it's still running in the background (for example while a game is open) brings its window back instead of showing a message.
- A launcher that has stopped responding is closed automatically the next time you open it.

### Changed
- The launcher now updates itself automatically when a new version is out (Windows asks for permission first). Turning off automatic updates on the About page turns this off too.
- When GitHub asks the launcher to slow down, the sidebar says "GitHub is busy" instead of "Offline".
- If a game doesn't start, or closes right after starting, the launcher says so and suggests what to do. If its program has gone missing (for example removed by Windows Security), Install downloads it again.

## [2.0.1] - 2026-10-30

### Added
- The Appalachian Trail is now available to install from Education. It downloads and updates from its own GitHub repository like the other games.

### Changed
- Removed the View on GitHub link from game details and the launcher update notes.

## [2.0.0] - 2026-10-30

### Added
- Games now download and install from GitHub. Each game has its own GitHub repository, and the launcher installs the latest release from it.
- Automatic game updates: when a new version of an installed game is released, the launcher downloads and installs it by itself (never while you're playing). You can turn this off on the About page and update by hand instead.
- Update buttons and an Update all button on the Installed page, with each update's release notes in the game's details.
- The launcher updates itself: when a new Ironite Launcher is released, a banner offers to update it, and the launcher closes, updates and opens again.
- Rewind Life is available as a free demo in Simulation, marked DEMO. It installs and updates from the Rewind Life repository.
- Texas Hold'em, Blackjack, Go Fish, Chess and Checkers are now available to install.
- Download size, version and a View on GitHub link in each game's details.
- The sidebar shows when the launcher last checked GitHub for updates, with a button to check now.
- New games can appear in the library without a launcher update.

### Changed
- Games are no longer built into the launcher, so the launcher's setup is now a small download. Games you installed with an earlier version are kept and are updated from GitHub.
- Downloads are checked against GitHub's checksums before they're installed.
- The launcher keeps running in the background after you close its window until downloads finish.
- What's New lists the latest version of every game.

## [1.9.0] - 2026-10-26

### Added
- Maple Lane Finds (v1.0.0), a cozy autumn hidden-object game, is now built into the launcher and playable from Hidden Object.
- Snugfit (v1.0.0), relaxing jigsaw puzzles, is now built into the launcher and playable from Puzzle.

### Changed
- The Hidden Object and Puzzle categories now list their games instead of general placeholders.

## [1.8.0] - 2026-10-20

### Added
- Euchre (v1.0.0) is now built into the launcher and playable from Card Games.
- Euchre joins the featured banner.
- New Board Games category with Chess, Mahjong and Checkers coming soon.

### Changed
- Euchre in Card Games now uses the game's own cover art.

## [1.7.0] - 2026-10-20

### Added
- Game compatibility: each game shows the systems it plays on (Windows, macOS, Linux or Web) on its tile, in its details and on the featured banner.
- The About page lists the launcher's platforms: Windows now, with macOS and Linux coming soon.

## [1.6.0] - 2026-10-20

### Added
- Old Maid (v1.0.0) is now built into the launcher and playable from Card Games.
- Old Maid joins the featured banner.

### Changed
- Old Maid in Card Games now uses the game's own cover art.

## [1.5.0] - 2026-10-20

### Added
- Simulation: Rewind Life, Lifeboat Simulator and Prison Cell Simulator added as Coming Soon.
- New Education category with The Appalachian Trail (Coming Soon).

### Changed
- The Simulation category now lists its upcoming games instead of a general placeholder.

## [1.4.0] - 2026-10-20

### Added
- New Simulation and Idle categories.
- Dookie Clicker added to Idle as a web game. It opens online in its own window with nothing to install, and its progress is saved on your PC.
- Opens and play time are tracked for web games too.

### Changed
- The launcher now uses the official Ironite logo in the sidebar, the app icon (desktop, Start menu, taskbar) and the installer.

## [1.3.0] - 2026-10-20

### Added
- Featured banner now rotates between Klondike and Solar Siege, with arrows and dots to switch.
- New Installed category: games you install appear there as well as in their own category, with a count in the sidebar.
- Install, Update and Uninstall for each game. Uninstalling removes the game files but keeps your saves and stats.
- Install progress shown on the game's button and tile.
- Game details window (click any game) with Play, Install and Uninstall.
- Tracks how many times each game is opened and how long it has been played, including the session in progress.
- Total time played shown on the Installed page and the About page.

### Changed
- The launcher keeps running in the background after you close its window until any open games are closed, so play time is always recorded.
- Games are no longer unpacked automatically on first Play; click Install first. Games unpacked by an earlier version count as installed.

## [1.2.0] - 2026-10-20

### Added
- Solar Siege (v1.1.0) is now built into the launcher and playable from the Arcade category.

### Changed
- What's New lists every game included in the launcher with its version.

## [1.1.0] - 2026-10-20

### Added
- Solar Siege added to the Arcade category as Coming Soon.

### Changed
- The Arcade category now lists Solar Siege instead of a general Arcade placeholder.

## [1.0.0] - 2026-10-20

### Added
- First release of Ironite Launcher for Windows.
- Game library with categories: Card Games, Hidden Object Games, Arcade, Puzzle and Word/Trivia.
- Klondike: Gold Rush Solitaire (v1.1.0) built into the launcher and playable with one click.
- Coming Soon placeholders: Texas Hold'em, Blackjack, Go Fish, Old Maid, Euchre, Hearts, Spades, Cribbage and Gin Rummy.
- Hidden Object, Arcade, Puzzle and Word/Trivia categories announced as Coming Soon.
- Play count and last-played tracking for each game.
- What's New screen showing this changelog, plus version number in the launcher.
- Windows installer that installs the launcher, with Start Menu and desktop shortcuts and an uninstaller in Apps & Features.
