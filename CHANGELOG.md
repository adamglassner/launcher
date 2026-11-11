# Changelog

All notable changes to Ironite Launcher are recorded here.
Versions follow Semantic Versioning (MAJOR.MINOR.PATCH): MAJOR for big overhauls,
MINOR for new games and features, PATCH for fixes.

## [3.1.4] - 2026-11-11

### Fixed
- A game's details show its whole cover at the top of the picture column, over a soft blurred copy of it, instead of a zoomed-in strip of the cover. The picture column's corners are rounded like the rest of the window.

## [3.1.3] - 2026-11-11

### Fixed
- Typing in the search box (and the other text boxes) starts at the left again, where "Search games" is, instead of partway across the box.

## [3.1.2] - 2026-11-11

### Changed
- Connecting Steam and Xbox for achievements has moved to a new Settings page in the sidebar. Your Steam and Xbox achievements still show on the Achievements page, which links to Settings.

## [3.1.1] - 2026-11-11

### Added
- Rewind Life's achievements: its 60 Memory Moments show on the Achievements page and in the game's details, grouped At Home, Around Town, Holidays & Seasons and School, from Rewind Life 1.10.0 (the launcher updates the demo by itself). Memory Moments found in older saves are added the next time you load them.

### Changed
- Rewind Life's description mentions its achievements.

## [3.1.0] - 2026-11-10

### Added
- Achievements for Ironite games. The Achievements page now starts with your Ironite games, listed by category with each medal, the date you unlocked it, and progress bars for the counting ones ("37 / 100"). Secret achievements stay secret until you unlock them in the game.
- Solar Siege is the first, with 54 achievements from version 1.4.0. Other Ironite games will appear on their own when they add achievements, without a launcher update.
- An Achievements box in an Ironite game's details, with how many you've unlocked, your latest medals, and a link to the full list.
- Achievements count even if you played the game from somewhere else, like the Solar Siege CD.

### Changed
- The Achievements page no longer says achievements for Ironite games are coming soon. Steam and Xbox achievements for your other games are listed under them, as before.

## [3.0.0] - 2026-11-10

### Changed
- Ironite Launcher has been rebuilt as a native Windows app, made the same way as Bytehawk Launcher (C# and WPF on .NET 8, installed with Inno Setup). It runs in its own window instead of a browser window, and no longer needs Microsoft Edge to show the launcher (web games still open in Edge or Chrome).
- It looks the same as before: the same library, featured banners, covers, colours and pages.
- Everything carries over from 2.x: your installed games, play time, sessions, favorites, hidden games, your own games (including Xbox and Microsoft Store games), cover art, achievement accounts and settings.
- Launcher updates work like Bytehawk Launcher's: Update now downloads the new installer, which runs on its own with just its progress bar and opens the launcher again when it's done.
- The installer replaces the 2.x install in place. Uninstalling no longer needs the old uninstaller.

### Added
- Open game folder in an installed game's details, and links to the install folder, games folder and log on the About page.
- New games added to the catalog after this version get their covers automatically.

## [2.16.0] - 2026-11-09

### Added
- Search: a search box at the top of the Library filters games by name as you type (Ctrl+F jumps to it).
- Sort Installed by recently played, most played, or A–Z.
- Favorites: click the star on any game (or in its details) to pin it to a Favorites section at the top of the Library.
- Hide games you're not interested in from their details. Show hidden brings them back.
- Desktop shortcuts: make one from a game's details. It starts the game through the launcher, so opens and play time are still counted.
- Download queue: installing several games lines them up one at a time and shows "Queued · 2 of 4".
- Size on disk for each installed game in its details, and the total on the About and Stats pages.
- Stats page: play time per week for the last 8 weeks (by game), plus a table of every game you've played.
- Recent sessions in a game's details: when you played and for how long.

### Fixed
- Xbox and Microsoft Store games are counted properly. An open and play time now count only once the game is actually running, not for the time Windows spends starting it.
- Your added games (including Xbox / Store games) are counted even when you start them outside the launcher, from the Xbox app, the Start menu or their own shortcut.

## [2.15.0] - 2026-11-09

### Changed
- Launcher updates now work like Bytehawk Launcher's. When a new version is out, a banner offers it: Update now downloads the installer (with progress and a Cancel button), then the installer runs on its own showing just its progress bar, with no questions to answer. The launcher closes and the installer opens it again when it's done. Release notes and a ✕ to hide the banner are next to the button.
- The launcher no longer updates itself without asking; games still update automatically.

### Fixed
- The launcher no longer flashes while a game is running. It was redrawing the whole library every second and a half as the play-time counter ticked, and reloading every cover picture each time. It now redraws only when something you can see changes, and keeps cover pictures cached.

## [2.14.1] - 2026-11-09

### Fixed
- The launcher no longer flashes while a game is running. It was redrawing the whole library every second and a half as the play-time counter ticked, and reloading every cover picture each time. It now redraws only when something you can see changes (play time in whole minutes), and keeps cover pictures cached.

## [2.14.0] - 2026-11-09

### Added
- Spades: A Brooklyn Rooftop Card Game is now available to install from Card Games (no longer Coming Soon). Bid your tricks with a partner, go Nil if you dare and watch the bags on a rooftop under string lights, against the rooftop regulars or with friends on the same computer or online, one to four players. It downloads and updates from its own GitHub repository like the other games, and it has new cover art and a spot in the featured carousel.

### Fixed
- Uninstalling the launcher now also closes Spades if it's running, like the other games.

## [2.13.0] - 2026-11-08

### Added
- Cribbage: A Lighthouse Card Game is now available to install from Card Games (no longer Coming Soon). Count fifteens, pairs and runs and peg to 121 (or 61) in the keeper's cottage at Seal Harbor Light, against the cottage regulars or with friends on the same computer or online, two to four players. It downloads and updates from its own GitHub repository like the other games, and it has new cover art and a spot in the featured carousel.

### Fixed
- Uninstalling the launcher now also closes Hearts, Gin Rummy and Cribbage if they're running, like the other games.

## [2.12.0] - 2026-11-08

### Added
- Gin Rummy: A Club Car Card Game is now available to install from Card Games (no longer Coming Soon). Knock, go gin or undercut your way across the prairie in the club car of the Starlight Limited against a new passenger every game, or play a friend on the same computer or online. It downloads and updates from its own GitHub repository like the other games, and it has new cover art and a spot in the featured carousel.

## [2.11.0] - 2026-11-08

### Added
- Achievements from other launchers for your non-Ironite games. In a game's Edit details, choose where its achievements come from: Steam (by the game's Steam app ID) or Xbox (by its Xbox title ID). Find fills these in automatically for games installed by Steam and for Xbox games.
- The Achievements page lists your linked games with how many achievements you've unlocked (and gamerscore for Xbox), and each game opens to show every achievement with its icon and when you got it.
- Steam achievements come from your public Steam profile (the account signed in to Steam on this PC is found automatically). Xbox achievements use a free OpenXBL key from xbl.io.

### Fixed
- Xbox and Microsoft Store games no longer keep showing as running after you close them. The launcher now recognizes the game by its app package as well as its folder and program names, and the game's details have an "It's closed" button just in case.

### Changed
- Achievements for Ironite games are still coming soon; the Achievements page says so at the top.

## [2.10.0] - 2026-11-08

### Added
- Add games from the Xbox app and Microsoft Store: Add a game has a new Xbox / Microsoft Store tab listing the games installed on this PC (with a search box). The launcher starts them through Windows, uses their own artwork, and tracks play time while they're open.
- Categories for your own games: put a game in one of the launcher's categories (it shows up there too) or make your own, like Racing or Shooters. Non-Ironite Games groups your games by category.
- Edit details for your own games: name, category, developer, players and a description.
- Custom cover art: choose a picture (or drop one onto the cover) to use as a game's cover, or go back to the game's icon.

### Changed
- Rename is now part of Edit details.

## [2.9.0] - 2026-11-08

### Added
- Add your own games: a new Non-Ironite Games category in the sidebar with an Add a game button. Pick any game's program on your PC (Browse opens the Windows file picker) and give it a name. It shows up with its own icon in Non-Ironite Games and Installed, and the launcher counts how often you open it and how long you play, just like the Ironite games.
- Your own games can be renamed, or removed from the launcher. Removing one never touches the game's files.

## [2.8.0] - 2026-11-07

### Added
- Hearts: A Paris Café Card Game is now available to install from Card Games (no longer Coming Soon). Dodge the hearts and the Queen of Spades against three café regulars, or play friends on the same computer or online. It downloads and updates from its own GitHub repository like the other games, and it has new cover art and a spot in the featured carousel.

## [2.7.0] - 2026-11-07

### Added
- Mahjong: Jade Moon Garden is now available to install from Board Games (no longer Coming Soon). It's Mahjong solitaire in a moonlit Chinese garden with twelve puzzles from Easy to Hard. It downloads and updates from its own GitHub repository like the other games, and it has new cover art and a spot in the featured carousel.

## [2.6.0] - 2026-11-07

### Added
- An Achievements page in the sidebar, marked Coming Soon.

## [2.5.0] - 2026-11-07

### Added
- A Friends page in the sidebar, marked Coming Soon: friends lists, seeing what friends are playing, and game invites are on the way.

### Fixed
- After updating itself, the launcher now opens again by itself. A small helper waits for the update to finish and then starts the new launcher, and the launcher reopens its window if it doesn't appear the first time.

## [2.4.1] - 2026-10-31

### Changed
- Keyhole's description now mentions its new Zen and Theme vaults.

## [2.4.0] - 2026-10-31

### Added
- Keyhole, the daily word vault, is now available to install from Word/Trivia. It downloads and updates from its own GitHub repository like the other games.

## [2.3.2] - 2026-10-31

### Changed
- The launcher's setup no longer lists the games. The Library in the launcher shows them all.

## [2.3.1] - 2026-10-31

### Changed
- Web games (Dookie Clicker) now always appear in Installed, ready to play, since they don't need installing.

## [2.3.0] - 2026-10-31

### Added
- Keyhole, a daily word-guessing game where you crack a three-lock vault, is now listed in Word/Trivia as Coming Soon, with its own cover art. It replaces the Word/Trivia placeholder tile.

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
