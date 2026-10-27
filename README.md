# Survivor's Toolkit

*Craft your 7d2d experience.*

A Windows mod manager for **7 Days to Die**, built on the player side of
[ModWright](https://github.com/AzraelGodKing/ModWright) (The Drop Pod): the same .NET 8 WinForms stack, the same
look and every one of its themes, cut down to what 7 Days to Die needs.

It finds the game through your Steam libraries (or a folder you pick) and reads every mod from both places the game
loads them: the game's own `Mods` folder and `%AppData%\7DaysToDie\Mods`. Nothing is changed until you press
**Save to game**, and nothing is changed while the game is running.

## Download

Each [release](https://github.com/AzraelGodKing/Survivor-s-Toolkit/releases) has three ways to get it, the same as
ModWright:

- `SurvivorsToolkit-<version>-Setup.exe`: installs the app for your Windows user (no administrator rights) with Start
  menu and desktop shortcuts, and keeps it up to date the way most desktop apps do: a separate updater
  ([Velopack](https://velopack.io)) swaps in the new version after the app has closed, downloading only what changed.
  Recommended. Uninstall it from Windows' **Installed apps**; your settings stay in `%LocalAppData%\SurvivorsToolkit`.
- `SurvivorsToolkit-<version>-win-x64.exe`: a single self-contained exe; no .NET install needed.
- `SurvivorsToolkit-<version>-win-x64-framework-dependent.zip`: a small folder build that needs the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Use it if Windows or your antivirus
  refuses to launch the large single-file exe.

All three update themselves (see **Updates** below).

## Using it

- **Mods:** tick a mod to enable it. **Save to game** (Ctrl+S) moves unticked mods to a `Mods-disabled` folder next
  to the `Mods` folder they came from, and ticked ones back. The game's own `0_TFP_Harmony` is always on. The
  **Load order** column shows the order the game loads them in (by folder name, A to Z). Ctrl+Z and Ctrl+Y undo and
  redo ticks; **Undo changes** goes back to what is on disk.
- **Install:** drop a mod's `.zip`, folder, or download link anywhere on the window, or press **Install...** (Ctrl+O)
  or **Tools > Install a mod from a link**. A link can be a direct link to a zip, or a GitHub repository or release page
  (its release zip is used). Every folder
  inside with a `ModInfo.xml` is a mod, however deep the zip put it. A mod that is already installed (same ModInfo
  `Name`) is replaced where it is, after the old copy is zipped into the mod backups. `.rar` and `.7z` need unpacking
  first. **Uninstall** sends a mod to the Recycle Bin.
- **Profiles:** saved sets of enabled mods, such as vanilla, your server, or a test setup. Picking one in the
  **Profile** box switches to it at once (exactly its mods are enabled); **Vanilla (no mods)** is always there and turns
  every mod off. **Save as...** keeps the ticked mods as a profile. **More > Play settings for this profile** gives it
  its own EasyAntiCheat setting and launch options, which Play uses instead of the ones in Options, and **Create a
  desktop shortcut** makes a shortcut that switches to it and starts the game. **More** also imports and exports
  profiles, copies a share code (`7DMM1:...`) to paste in a chat, and adds a profile from one.
- **Problems:** mods with no or a broken `ModInfo.xml`, mods unpacked one folder too deep (**Fix** moves the folder
  up), two enabled mods with the same `Name` (the game loads only the first), old-format ModInfo files, code mods
  with EasyAntiCheat forced on, and a missing `0_TFP_Harmony` (**Fix** asks Steam to verify the game files).
  It also reads each enabled mod's `Config` XML: two mods that `set`, `remove` or `setattribute` the same xpath in the
  same file (whatever their quotes and spacing; the one that loads last wins), two mods that add an item, block or
  buff with the same name, and XML files that aren't valid. A mod whose description says it needs another ("Needs the
  Bait mod", "Requires SCore") is flagged when that mod is disabled or missing; one needed only on the server is left
  alone when it isn't installed here. Mods whose name or description says they are for an older game ("A20", "for
  V1.0") are flagged against the game version read from the log, which is shown beside the game folder.
- **Play:** starts the game. With EAC on it goes through Steam and the game's launcher; with EAC off it starts
  `7DaysToDie.exe` directly. On **Auto** (the default), EAC is off whenever an enabled mod has code (a DLL). Launch
  options, and a backup of the newest save before each start, are under **Options > Play**. The **▾** beside Play
  plays with or without EAC this once, and starts a **dedicated server** on this PC the way the game's
  `startdedicated.bat` does (Steam's "7 Days to Die Dedicated Server", a folder you pick, or the game itself), after
  backing up its `serverconfig.xml`.
- **Dedicated server (Options):** the server folder, its config file (shown with its name, port, world and EAC, and a
  warning when code mods meet `EACEnabled` true), extra launch options, and `serverconfig.xml` backups: **Back up
  now**, **Restore...** (the file it replaces is backed up first) and one before every start (the newest 20 are kept).
- **Save backup before mod changes:** with **Options > Play > Back up the newest save before mods are enabled,
  disabled, switched or updated** on, Save to game, switching profiles and installs zip the newest save first.
- **Test** and **Read the log:** Test starts the game and follows its log; Read the log reads the last one. Errors
  and warnings are grouped by message, and each names the mod most likely at fault: a mod named in it (XML patch
  errors name the mod), a file in its folder, or its code in the stack trace (namespaces read from the mod's DLLs,
  never run). **Every line** shows the whole log instead, each line with the mod its entry points at, filtered by mod,
  level (everything, warnings and errors, errors only), XML patch problems, and text; lines about XML patching (a mod's
  xpath that failed) are highlighted, and it stays at the end while following. Right-click a mod > **Its lines in the
  game log** opens it filtered to that mod.
- **Server, client or both:** the **Side** column says where each mod has to be installed for a multiplayer game.
  It comes from the mod's own description when it says so ("Server only: players install nothing", "Only players
  need it", "Install on the server AND every player's game"). Otherwise it is a guess from the mod's files, marked
  with `?`: XML alone works from the server, which sends it to players; HUD windows (XUi), textures, sounds and asset
  bundles have to be on each player's PC; code counts as both. Right-click a mod > **Side** to set it yourself.
- **Server sync:** **Server...** (or **Tools > Server sync**) compares your enabled mods with a dedicated server's
  Mods folder and pushes the ones the server needs: those whose Side is Server or Both. The server is reached through a
  folder (a shared folder, a mapped drive, or a dedicated server on this PC) or FTP/FTPS; the FTP password is kept
  encrypted for your Windows account. Only files that differ are sent, and files a mod no longer has are removed.
  Mods that are only for players, or disabled here, can be removed from the server (tick them); mods the server has
  that this PC doesn't are left alone. Restart the server afterwards. SFTP isn't supported.
- **Server panel and console:** **Tools > Server panel and console** talks to a server's telnet console, so you can see
  whether it is up, who is on it (health, deaths, zombies killed, level, ping), the in-game day and clock, the next
  blood moon and the game's version, refreshed every 15 seconds. Under it is an admin console: type any command, or use
  the buttons for **Say**, **Save the world**, **Shut down** and the command list, and right-click a player to kick or
  ban them. The up and down arrows walk back through what you have typed, and anything that can't be taken back
  (`shutdown`, `kick`, `ban`) asks first. It works with a server on this PC and with a hosted one: the server needs
  `TelnetEnabled` true, a `TelnetPassword` and its `TelnetPort` reachable. **Read the server's config** fills the
  address, port, password and blood moon frequency in from the dedicated server's own `serverconfig.xml` when there is
  one on this PC; for a hosted server, copy them from your host's panel. The password is kept encrypted for your
  Windows account.
- **Worlds and saves:** **Saves...** lists your worlds with the saves in each, when they were last played and what
  they take up, and the map the game built for a random gen world (`GeneratedWorlds`), which is usually the biggest
  thing in there. Rename a save, or a whole world (its map is renamed with it), back a save up to a zip, restore one
  from a backup (what it replaces is backed up first), and delete a save or a world - every delete backs the saves up
  first. Deleting only a world's map frees that space and keeps the saves: the game builds the map again from the
  world's seed when it needs it.
- **XML tweak builder:** **Tools > Make an XML tweak mod** builds a small XPath mod from a form. Pick one of the game's
  own config files (`Data/Config`), find the item, block or entity in it, pick a property - the list shows what the game
  has now - and type the new value. "Every one of them in this file" sets it on all of them at once. The tweaks you add
  are shown as the XML they will become, and **Make the mod** writes an ordinary mod folder with a `ModInfo.xml` and a
  `Config` file of `<set>` patches. The xpath is built from the real document, so it points at something that is
  actually there.
- **Game update warning:** the app remembers which build of the game it last saw (Steam's build id, the version in the
  log and the exe's date). When 7 Days to Die has been updated, it says so once and names the enabled mods most likely
  to break: the ones with code first, since the game's own DLLs changed under them, then any that say they are for an
  older game.
- **Crash helper:** **Tools > What went wrong last time?** reads the game's last log, says whether the session ended
  badly (the game said so, or the log just stops), lists the errors as the log viewer groups them, and names the mod
  most of them point at - with a button that shows you that mod in the list. When the last session ended badly, the
  app says so once at the top of the window the next time it starts.
- **Scheduled restarts:** **Scheduled restarts** in the server panel warns everyone in chat ("the server restarts in
  15 minutes", again at 5 and 1, in your words), saves the world, shuts the server down, and - for a server on this PC -
  starts it again and says it is back. Give it one or more times of day. The schedule runs while that window is open;
  for one that runs whether the app is open or not, point Windows' Task Scheduler at `SurvivorsToolkit restart`.
  **Restart now** does the same with the first warning going out at once, and shows you the whole timeline first.
- **Client pack:** **Tools > Export a client pack for players** zips the enabled mods players need (Client only, or
  Server + clients) into one file, with the profile and a readme inside, so players' mods match the server's. They
  drop it on Survivor's Toolkit, or unzip it into their game folder.
- **Settings:** **Settings...** beside the list (or right-click > **Edit settings...**) opens a mod's settings files
  (`settings.txt`, `*.cfg` and the like, not readmes or examples). A file of `Key=value` lines is shown as a form,
  each setting with the author's comment above it and on/off switches as drop-downs; **Text** edits any file as
  text, such as a list like `entities.txt`. Saving rewrites only the lines you changed, and keeps your copy: when the
  mod is updated or reinstalled, your values go back into the new version's file (new settings it adds keep their
  defaults).
- **Mod updates:** **Tools > Check mods for updates on GitHub** reads the releases of every mod whose Website is a GitHub
  repository (tags `<ModName>-v<version>`, or `v<version>` for a repository of one mod) and lists the newer ones;
  **Update ticked** downloads and installs them over the old copies, after a backup, keeping your edited settings.
- **Details beside the list:** the picked mod's ModInfo, where it is, whether it has code, its size and your note.
  Right-click a mod for **Note...**, **Colour**, its folder and website, and **Uninstall**.
- **Tools:** **Find the broken mod for me** (enables half the suspects each round until one is left, then puts your
  mods back), the Mods and backup folders, and **Verify the game files in Steam**.
- **Saves...:** every save with its world, when it was last played and its size, with **Back up** to a zip.
- **Options:** theme (Wasteland, the default, and Wasteland Light, styled after 7 Days to Die; Dark, Light, Kats, Koneko, Serina, Wolfie, Vickra, Kitomi, Szendoo, Triphlx, Tohma, Skywolf,
  Hunter, or Match Windows; Windows High Contrast always wins), text size, the game and data folders, where new mods
  are installed, EasyAntiCheat and launch options.

- **Updates:** at start-up the app asks GitHub for a newer release and offers **Update now**, **Release notes** or
  **Later**, with a box to stop reminding you about that version. **Update now** downloads the new version and checks it
  against the release's checksums, then offers **Restart now** or **When I close the app**; until it is
  installed, a **Restart now** link at the bottom of the window says it is ready. A copy installed with the
  Setup.exe is updated by Velopack's updater once the app has closed. The standalone exe and the zip build update
  themselves from the release's exe or zip: the old files are kept as `<name>.update-old` until the next start, and
  put back if Windows (or antivirus) won't run the new one. With
  **Options > About > Download new versions by themselves** on, a new version is downloaded in the background and
  installed when you close the app, without asking. After an update the changes since your version are shown once
  (from `CHANGELOG.md`, built into the app); **What's new** shows them all. Only GitHub's public list of releases is
  read. Turn the check off, or **Check now**, under **Options > About**. A copy that can't update itself (a folder
  it can't write to, or `dotnet run`) offers **Download** through the browser instead.

Settings, profiles (the `modlists` folder) and backups are in `%LocalAppData%\SurvivorsToolkit`.

## Command line

```
SurvivorsToolkit list [--json]         the installed mods (* = enabled)
SurvivorsToolkit problems [--json]     problems with the enabled mods (exit code 1 on errors)
SurvivorsToolkit lists                 the saved profiles (* = last used)
SurvivorsToolkit switch <profile>      enable exactly the mods of a profile ("vanilla" turns every mod off)
SurvivorsToolkit play [<profile>]      start the game, after switching to the profile
SurvivorsToolkit install <zip|folder|link>  install a mod
SurvivorsToolkit updates               newer versions of installed mods on GitHub
SurvivorsToolkit server                start the dedicated server on this PC
SurvivorsToolkit status [--json]       the server's day, players and blood moon, over its telnet console
SurvivorsToolkit rcon <command>        send one command to that console ("say hello", "saveworld")
SurvivorsToolkit restart [--now]       warn the players, save the world, shut the server down, start it again
```

A desktop shortcut to `SurvivorsToolkit.exe play "Horde night"` starts that setup in one click, and a Windows
scheduled task running `SurvivorsToolkit.exe restart` at 05:00 restarts the server with its warnings every morning.

## Build

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). No game files are needed.

```powershell
dotnet build SurvivorsToolkit.sln -c Release
dotnet test SurvivorsToolkit.sln
```

The app is `src\SurvivorsToolkit\bin\Release\net8.0-windows\SurvivorsToolkit.exe`. The **Build**
GitHub workflow builds and tests every push and pull request and uploads a self-contained single-file exe.

`src/SurvivorsToolkit.Core` holds everything that needs no WinForms (finding the game, reading mods, installing,
modlists, problems, the log, saves, the command line) and is what the tests cover. `src/SurvivorsToolkit` is the
window. `Theme.cs` is ModWright's, plus the two Wasteland themes.

## Releasing

On `test`, bump `<Version>` in `Directory.Build.props` and add a `## <version>` section to `CHANGELOG.md`. When
`test` is ready, run **Promote test and release** (Actions tab; tick **dry_run** to only check and test). It merges
`main` into `test` if `test` doesn't already contain it, runs the tests, fast-forwards `main` to `test`, tags it
`v<version>` and leaves `test` at the same commit, all in one push. The tag starts the **Release** workflow, which
refuses a version with no changelog section, tests, publishes the Setup.exe with its Velopack update packages
(`*.nupkg`, `releases.win.json`, `assets.win.json`, `RELEASES`; a delta from the previous release when there is one),
`SurvivorsToolkit-<version>-win-x64.exe`, the framework-dependent zip (all ReadyToRun, so they start quickly) and
`SHA256SUMS.txt`, and creates the release `v<version>` with that section as its notes, which
is what the app's update check looks for. The same section is what the app shows once after updating to that version.
A version with a `-` (such as `0.4.0-beta.1`) becomes a pre-release, which the update check skips.

Promote test and release needs a `RELEASE_TOKEN` repository secret: a fine-grained personal access token for this
repository with **Contents** and **Workflows** read and write. The built-in token can't push commits that change
workflow files, and a tag it pushes wouldn't start the Release workflow.

**Release** can also be run by hand on any branch, optionally with extra notes; it tags the commit it ran on.

## Not carried over from ModWright

The Launch Pad (Workshop uploads; 7 Days to Die has no Workshop), SteamCMD, Workshop and Thunderstore downloads,
load-order sorting (the game has no load-order file), and RimWorld's instances, XML and texture tools.

## History and branches

Survivor's Toolkit started as the 7 Days Mod Manager in
[7d2d_mods](https://github.com/AzraelGodKing/7d2d_mods) (`ModManager/`), and this repository keeps its history. The
first run copies the old settings, modlists and backups from `%LocalAppData%\SevenDaysModManager` to
`%LocalAppData%\SurvivorsToolkit`.

Work goes into the long-lived `test` branch first, then **Promote test and release** brings `test` into `main`. `test`
is never deleted: if a `test` -> `main` pull request is merged instead and GitHub deletes the branch, the **Keep test
branch** workflow puts it back at the new `main`.
