using System.Text.Json;

namespace SurvivorsToolkit.Core;

/// <summary>
/// The command line, for scripts and desktop shortcuts:
/// <code>
/// SurvivorsToolkit list [--json]        the installed mods (* = enabled)
/// SurvivorsToolkit problems [--json]    problems with the enabled mods (exit code 1 on errors)
/// SurvivorsToolkit lists                the saved profiles (modlists)
/// SurvivorsToolkit switch &lt;profile&gt;     enable exactly the mods of a profile ("vanilla": none)
/// SurvivorsToolkit play [&lt;profile&gt;]     start the game, after switching to the profile
/// SurvivorsToolkit install &lt;zip|folder|link&gt; install a mod
/// SurvivorsToolkit updates              newer versions of installed mods on GitHub
/// SurvivorsToolkit server               start the dedicated server on this PC
/// SurvivorsToolkit status [--json]      the server's day, players and blood moon over its telnet console
/// SurvivorsToolkit rcon &lt;command&gt;       send one command to the server's telnet console
/// SurvivorsToolkit restart [--now]      warn the players, save the world, shut the server down (and start it again)
/// </code>
/// </summary>
public static class Cli
{
    public static readonly string[] Commands = { "list", "problems", "lists", "switch", "play", "install", "updates", "server", "status", "rcon", "restart", "help", "--help", "-h", "/?" };

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0].ToLowerInvariant());

    public static int Run(string[] args, TextWriter o, AppSettings s)
    {
        var cmd = args[0].ToLowerInvariant();
        var json = args.Contains("--json");
        var rest = args.Skip(1).Where(a => a != "--json").ToList();
        if (cmd is "help" or "--help" or "-h" or "/?") { o.WriteLine(Help); return 0; }

        // These two only talk to the server's console, so they don't need the game installed here.
        if (cmd is "status" or "rcon" or "restart") return Console_(cmd, rest, s, o, json, args.Contains("--now")).GetAwaiter().GetResult();

        var steam = SteamLibraries.Find();
        var install = SevenDays.Detect(steam, s.GameFolder, s.UserDataFolder);
        if (install == null) { o.WriteLine("7 Days to Die was not found. Open the app and pick the game folder."); return 2; }
        var scan = ModLibrary.Scan(install);
        var store = new ModlistStore(AppSettings.ModlistFolder);
        try
        {
            switch (cmd)
            {
                case "list":
                    SideInfo SideOf(InstalledMod m) => ModSides.Of(m, s.Sides.TryGetValue(m.Key, out var c) ? c : null);
                    if (json) o.WriteLine(JsonSerializer.Serialize(scan.Mods.Select(m => new { m.Key, m.DisplayName, version = m.Info?.Version, m.Enabled, m.HasCode, side = SideOf(m).Side.ToString(), sideGuessed = SideOf(m).Guessed, location = m.Location.ToString(), m.Folder }), new JsonSerializerOptions { WriteIndented = true }));
                    else foreach (var m in scan.Mods) o.WriteLine($"{(m.Enabled ? "*" : " ")} {m.DisplayName}{(m.Info?.Version is { Length: > 0 } v ? " " + v : "")}{(m.HasCode ? "  [code]" : "")}{(m.Locked ? "" : "  [" + SideOf(m).Short + "]")}  ({m.FolderName})");
                    return 0;
                case "problems":
                {
                    var found = Problems.Check(scan, scan.Mods.Where(m => m.Enabled).Select(m => m.Id).ToHashSet(), s.Eac, ModAnalysis.For(install));
                    if (json) o.WriteLine(JsonSerializer.Serialize(found.Select(p => new { severity = p.Severity.ToString(), mod = p.ModId, p.Text }), new JsonSerializerOptions { WriteIndented = true }));
                    else if (found.Count == 0) o.WriteLine("No problems.");
                    else foreach (var p in found) o.WriteLine($"{p.Severity}: {p.Text}");
                    return found.Any(p => p.Severity == Severity.Error) ? 1 : 0;
                }
                case "lists":
                    foreach (var l in store.All().Prepend(Modlist.Vanilla()))
                        o.WriteLine($"{(string.Equals(l.Name, s.Modlist, StringComparison.OrdinalIgnoreCase) ? "*" : " ")} {l.Name} ({l.Mods.Count} mods){(l.Eac is { } e ? ", EAC " + e : "")}{(l.LaunchOptions is { Length: > 0 } lo ? ", options: " + lo : "")}");
                    return 0;
                case "switch":
                    if (rest.Count == 0) { o.WriteLine("Usage: switch <modlist>"); return 2; }
                    return Switch(rest[0], scan, store, s, o) ? 0 : 1;
                case "play":
                    if (rest.Count > 0)
                    {
                        if (!Switch(rest[0], scan, store, s, o)) return 1;
                        scan = ModLibrary.Scan(install);
                    }
                    var r = GameRunner.Play(scan, s, steam.SteamFolder, s.Modlist is { } current ? store.Get(current) : null);
                    o.WriteLine("Starting 7 Days to Die. " + r.Why + ".");
                    return 0;
                case "server":
                {
                    var server = DedicatedServer.Find(steam, s.ServerFolder, install);
                    if (server == null) { o.WriteLine("No dedicated server found. Install \"7 Days to Die Dedicated Server\" from Steam's Tools, or pick its folder in the app's Options."); return 1; }
                    var backup = GameRunner.StartServer(server, s);
                    o.WriteLine($"Starting the dedicated server in {server.Folder}{(backup != null ? $" (config backed up to {backup})" : "")}.");
                    return 0;
                }
                case "install":
                    if (rest.Count == 0) { o.WriteLine("Usage: install <zip, folder or link>"); return 2; }
                    var into = s.InstallToUserData ? install.UserMods : install.GameMods;
                    if (s.BackupSaveBeforeModChange && Saves.BackupNewest(install, AppSettings.SaveBackupFolder) is { } saved) o.WriteLine("Newest save backed up to " + saved);
                    if (ModDownloader.IsLink(rest[0])) o.WriteLine("Downloading " + rest[0] + " ...");
                    var installed = ModDownloader.IsLink(rest[0])
                        ? ModDownloader.InstallAsync(rest[0], scan, into, AppSettings.BackupFolder).GetAwaiter().GetResult()
                        : ModInstaller.Install(rest[0], scan, into, AppSettings.BackupFolder);
                    foreach (var done in installed)
                    {
                        o.WriteLine((done.Replaced != null ? "Updated " : "Installed ") + done.Name + " in " + done.Folder);
                        var kept = new ModSettingsStore(AppSettings.ModSettingsFolder).Restore(done.Key, done.Folder);
                        if (kept.Count > 0) o.WriteLine("  your settings put back: " + string.Join(", ", kept));
                    }
                    return 0;
                case "updates":
                {
                    var found = ModUpdates.CheckAsync(scan.Mods, (u, ct) => ModDownloader.Api.GetStringAsync(u, ct)).GetAwaiter().GetResult();
                    if (found.Count == 0) o.WriteLine("No newer versions on GitHub (only mods whose Website is a GitHub repository are checked).");
                    foreach (var u in found) o.WriteLine($"{u.Name}: {u.Current} -> {u.Latest}  {u.ZipUrl ?? u.PageUrl}");
                    return 0;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or HttpRequestException or TaskCanceledException)
        {
            o.WriteLine(e.Message);
            return 1;
        }
        o.WriteLine(Help);
        return 2;
    }

    /// <summary>status and rcon: one telnet link to the server in the settings, then the answer as text or JSON.</summary>
    static async Task<int> Console_(string cmd, List<string> rest, AppSettings s, TextWriter o, bool json, bool now = false)
    {
        if (!s.Telnet.IsSet) { o.WriteLine("No server console is set up. Open the app, Tools > Server panel and console, and fill in the address, port and password."); return 2; }
        if (cmd == "restart") return await RestartAsync(s, o, now);
        if (cmd == "rcon" && rest.Count == 0) { o.WriteLine("Usage: rcon <command>   for example  rcon \"say hello\""); return 2; }
        try
        {
            using var console = await TelnetConsole.ConnectAsync(s.Telnet.Host, s.Telnet.Port, Secrets.Unprotect(s.Telnet.ProtectedPassword));
            if (cmd == "rcon")
            {
                o.WriteLine(await console.SendAsync(string.Join(' ', rest), CancellationToken.None));
                return 0;
            }
            var status = await ServerStatusReader.QueryAsync(console, s.Telnet.BloodMoonFrequency);
            if (json) o.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
            else
            {
                o.WriteLine($"{s.Telnet.Describe}: up, {status.Clock}, {status.Players} player{(status.Players == 1 ? "" : "s")}, blood moon {status.BloodMoon}{(status.Version is { } v ? ", game " + v : "")}");
                foreach (var p in status.PlayerList) o.WriteLine($"  {p.Name}  health {p.Health}, level {p.Level}, ping {p.Ping}");
            }
            return 0;
        }
        catch (ServerConsoleException e)
        {
            o.WriteLine(e.Message);
            return 1;
        }
    }

    /// <summary>
    /// restart: the warnings, the save and the shutdown, and the server here started again when the plan says so.
    /// Made for Windows' Task Scheduler, which can run it whether the app is open or not.
    /// </summary>
    static async Task<int> RestartAsync(AppSettings s, TextWriter o, bool now)
    {
        var plan = s.Restarts;
        var warn = now ? 0 : plan.WarnMinutes.Where(m => m > 0).DefaultIfEmpty(0).Max();
        var runAt = DateTime.Now.AddMinutes(warn);
        var steps = Restarts.Timeline(now ? new RestartPlan { WarnMinutes = new List<int>(), Message = plan.Message, BackMessage = plan.BackMessage, SaveWorld = plan.SaveWorld, StartAgain = plan.StartAgain, StartAgainAfterSeconds = plan.StartAgainAfterSeconds, BackAfterSeconds = plan.BackAfterSeconds } : plan, runAt);
        o.WriteLine($"Restarting {s.Telnet.Describe} at {runAt:HH:mm}:");
        try
        {
            await Restarts.RunAsync(steps, async (step, ct) =>
            {
                o.WriteLine($"{DateTime.Now:HH:mm:ss}  {step.Describe}");
                if (step.Step == RestartStep.StartAgain)
                {
                    var steam = SteamLibraries.Find();
                    var game = SevenDays.Detect(steam, s.GameFolder, s.UserDataFolder);
                    if (DedicatedServer.Find(steam, s.ServerFolder, game) is not { } server) { o.WriteLine("  no dedicated server on this PC to start"); return; }
                    GameRunner.StartServer(server, s);
                    return;
                }
                using var console = await TelnetConsole.ConnectAsync(s.Telnet.Host, s.Telnet.Port, Secrets.Unprotect(s.Telnet.ProtectedPassword), ct);
                var answer = await console.SendAsync(step.Command!, ct);
                if (answer.Trim().Length > 0) o.WriteLine("  " + answer.Replace("\n", "\n  "));
            }, () => DateTime.Now, (wait, ct) => Task.Delay(wait, ct), CancellationToken.None);
            return 0;
        }
        catch (ServerConsoleException e)
        {
            o.WriteLine(e.Message);
            return 1;
        }
    }

    static bool Switch(string name, LibraryScan scan, ModlistStore store, AppSettings s, TextWriter o)
    {
        var list = store.Get(name);
        if (list == null) { o.WriteLine($"No profile called \"{name}\". Saved profiles: {string.Join(", ", store.All().Select(l => l.Name).Prepend("vanilla"))}"); return false; }
        if (GameRunner.IsRunning()) { o.WriteLine("7 Days to Die is running; close it first."); return false; }
        var (_, missing) = list.Match(scan);
        if (s.BackupSaveBeforeModChange && ModLibrary.Changes(scan, list.EnabledIds(scan)).Count > 0
            && Saves.BackupNewest(scan.Install, AppSettings.SaveBackupFolder) is { } zip) o.WriteLine("Newest save backed up to " + zip);
        var moved = ModLibrary.Apply(scan, list.EnabledIds(scan));
        s.Modlist = list.Name;
        s.Save();
        o.WriteLine($"Switched to \"{list.Name}\" ({moved} mod{(moved == 1 ? "" : "s")} moved).");
        foreach (var m in missing) o.WriteLine($"  not installed: {m.DisplayName ?? m.Name}");
        return true;
    }

    public const string Help = """
        Survivor's Toolkit

          SurvivorsToolkit                       open the window
          SurvivorsToolkit list [--json]         the installed mods (* = enabled)
          SurvivorsToolkit problems [--json]     problems with the enabled mods (exit code 1 on errors)
          SurvivorsToolkit lists                 the saved profiles (modlists; * = last used)
          SurvivorsToolkit switch <profile>      enable exactly the mods of a profile ("vanilla" turns every mod off)
          SurvivorsToolkit play [<profile>]      start the game, after switching to the profile
          SurvivorsToolkit install <zip|folder|link>  install a mod (an installed copy is replaced, after a backup)
          SurvivorsToolkit updates               newer versions of installed mods on GitHub
          SurvivorsToolkit server                start the dedicated server on this PC (its config is backed up first)
          SurvivorsToolkit status [--json]       the server's day, players and blood moon, over its telnet console
          SurvivorsToolkit rcon <command>        send one command to that console ("say hello", "saveworld")
          SurvivorsToolkit restart [--now]       warn the players in chat, save the world, shut the server down, and
                                                 start it again if the plan says so (--now skips the warnings)
        """;
}
