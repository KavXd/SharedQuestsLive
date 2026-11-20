# Changelog

This project uses [Semantic Versioning](https://semver.org/).

## 1.0.1 - 2026-11-20

- Fixed: items a friend handed in (and weapons built, items sold to traders) didn't show until a
  restart. The game keeps those on the profile rather than in the quest's counter list, and only the
  counter list was being checked. Kill-style counters and finished steps already worked.
- Fixed: after some menus the game uses a fresh copy of the profile, and syncing could stop with
  "The menu's quest list isn't for the current profile yet". It now uses the newest quest list for
  the same player, and logs when it finds it again.
- Old quest lists are no longer kept after a newer one is in use.

## 1.0.0 - 2026-11-20

First release.

- Shows quest progress from your Shared Quests group in your game within a few seconds, without a
  raid or restart: counters (kills, items handed in), finished steps and quests ready to turn in.
- Shows a short message when a friend's progress is added.
- Paused during raids and in the hideout; turns itself off on the Fika headless client.
