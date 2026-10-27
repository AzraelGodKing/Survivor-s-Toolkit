# Changelog

Each `## <version>` section is shown in the app once after it updates to that version (and all of them under
**Options > About > What's new**), and the Release workflow uses it as the release's notes.

## 0.4.0

- **Save the server's log to files.** **Save the log to files...** in the server panel (or **Tools > Save the
  server's log to files**) keeps the server's errors, warnings and every line in three files a day
  (`errors-`, `warnings-` and `everything-<date>.log`), each one only when it is ticked. A new set starts at midnight
  and days older than the ones you keep (14 unless you change it) are deleted. The log comes from the dedicated
  server's log file on this PC, or from the telnet console for a hosted server, which connects again by itself when
  the server restarts. It can start when the app opens, and `SurvivorsToolkit serverlog` does the same with no window.
- **Share a day of logs.** **Zip it to share** (or the last 7 days) makes a zip of that day's files, shows it in
  Explorer and puts it on the clipboard, ready to paste into Discord or an email.

## 0.3.0

- **Tools in the header bar.** SERVER, SYNC, SAVES, LOG, CRASH and TWEAKS sit next to MODS and OPTIONS and open their
  windows in one click (Alt+E, Y, A, L, C, T). Clicking a tab whose window is already open brings that window back
  instead of opening a second copy.
- Tools that need the game say so when it hasn't been found yet.
- On a narrow window the tagline hides so the tabs keep their room.

## 0.2.0

- **Updates install when you close the app.** After **Update now** downloads and checks a new version, pick
  **Restart now** or **When I close the app**. Until then a line at the bottom of the window says it is ready, with a
  **Restart now** link. Unsaved ticks are asked about first, as with any close.
- **Automatic updates (optional).** **Options > About > Download new versions by themselves** fetches a new version
  in the background at start-up and installs it when you close the app, without asking. It is off unless you turn it
  on, and a version you said not to be reminded about is left alone.
- **What's new.** After an update the changes since the version you had are shown once. **Options > About > What's
  new** shows every version.
- **Download through the browser** when this copy can't replace itself (a folder it can't write to, or a build that
  isn't the single-file exe).
- **A faster, smoother window.**
  - Scanning no longer freezes the window: finding the game, working out each mod's side (which walks the mod's
    folder) and looking for its settings files now happen in the background with the rest of the scan.
  - The problem check runs again only when the ticks, the scan or EasyAntiCheat change, not on every filter key,
    sort or colour change. Ticking several mods quickly refills the list once.
  - Typing in the filter box rebuilds the list once you pause.
  - The game's log is read in the background: at start-up for the crash note, and in the log window.
  - The mod and problem lists are drawn double-buffered, so they don't flicker.
  - The exe is compiled ahead of time (ReadyToRun), so the app opens faster.

## 0.1.0

The first release of Survivor's Toolkit, the 7 Days to Die mod manager built on ModWright's Drop Pod.

- **Mods:** enable and disable mods in both Mods folders, install from a zip, folder or link, profiles with share
  codes, problems (XML conflicts, missing requirements, old game versions), server, client or both, notes and colours.
- **Play and test:** EasyAntiCheat on, off or automatic, launch options, the log grouped by mod, a crash helper and a
  warning when the game has been updated.
- **Servers:** a dedicated server on this PC, server sync over a folder or FTP, a telnet panel and admin console, and
  scheduled restarts.
- **Worlds and saves:** backups, restores and a world manager, and an XML tweak builder.
- **Updates:** the app updates itself from GitHub releases, checked against SHA256SUMS.txt.
