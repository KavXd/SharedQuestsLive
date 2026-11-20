# Shared Quests Live

A client plugin for **SPT 4.1.x** that goes with the [Shared Quests](https://github.com/KavXd/SharedQuests)
server mod. It shows your group's quest progress in your game **within a few seconds**, instead of
after your next raid or a restart.

- Version: 1.0.0
- SPT: 4.1.x (tested on 4.1.6)
- Required: [Shared Quests](https://github.com/KavXd/SharedQuests) **1.1.0 or newer** on the server
- Optional: [Fika](https://github.com/project-fika)
- License: MIT

## What it does

While you're in the menus, the plugin asks the server every few seconds for your quests. When a
friend in your group has made progress, it's added to your quest list straight away:

- **Counters:** kills, items handed in and other progress.
- **Finished steps.**
- **Ready to turn in:** when every step is done, the quest becomes ready for you to turn in.

You get a short message, e.g. *"Shared Quests: progress from your group on Shooting Cans"*.

It only moves quests forward. It never turns in, starts, fails or removes a quest, so you still
turn quests in yourself and get your own rewards. It's paused during raids and in the hideout.

## Install

Every player who wants live progress installs it. Players without it still get their group's
progress as before (after a raid or a restart).

1. Make sure the server has **Shared Quests 1.1.0 or newer**, with `"liveSync": true` in its
   `config.json` (that's the default).
2. Extract the zip into your SPT game folder, so you get
   `BepInEx/plugins/SharedQuestsLive/SharedQuestsLive.dll`.
3. Start the game. `BepInEx/LogOutput.log` shows `[Shared Quests Live] Loaded`.

The **Fika headless client doesn't need it**; if it's there, it turns itself off.

## Settings

In `BepInEx/config/akvxxx16.sharedquests.live.cfg`, or in-game with the BepInEx Configuration Manager (F12):

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Turns live progress on or off. |
| `Check every (seconds)` | `5` | How often it asks the server while you're in the menus (2 to 60). |
| `Show a message` | `true` | Show a message when a friend's progress is added. |

## How it works

The plugin sends the server what your game currently has, and the server answers with your quests
as it has them. The server keeps what your game reported, so progress the plugin added isn't
counted again as your own at the end of your next raid.

## Uninstall

Delete `BepInEx/plugins/SharedQuestsLive`.

## Building from source

Needs the [.NET SDK](https://dotnet.microsoft.com/download) and an SPT 4.1 game folder whose
`EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` is SPT-patched (start the game once with the
SPT launcher).

```
dotnet build -c Release -p:GameDir="C:\path\to\SPT"
```

The plugin is `bin/Release/netstandard2.1/SharedQuestsLive.dll`.

## Source

All source code is in this repository: https://github.com/KavXd/SharedQuestsLive
The release DLL is built from it unchanged and isn't obfuscated.
