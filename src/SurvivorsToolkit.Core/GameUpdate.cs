namespace SurvivorsToolkit.Core;

/// <summary>
/// Which build of the game is installed, as far as this PC can tell: Steam's build id (it changes with every update),
/// the version the newest log states, and when the game's exe was last written.
/// </summary>
public sealed record GameBuild(string? SteamBuildId = null, string? Version = null, DateTime? ExeChanged = null)
{
    /// <summary>Nothing known yet (a first run, or no Steam).</summary>
    public bool Known => SteamBuildId != null || Version != null || ExeChanged != null;

    /// <summary>What to show the player: the version when there is one, else the build id or the date.</summary>
    public string Describe =>
        Version ?? (SteamBuildId != null ? "build " + SteamBuildId : ExeChanged?.ToString("d") ?? "an unknown version");

    /// <summary>
    /// Whether this looks like another build than <paramref name="other"/>. Only the parts both sides know are
    /// compared, so a missing Steam or a game that hasn't run yet doesn't count as an update.
    /// </summary>
    public bool DiffersFrom(GameBuild other)
    {
        if (SteamBuildId != null && other.SteamBuildId != null) return SteamBuildId != other.SteamBuildId;
        if (Version != null && other.Version != null) return Version != other.Version;
        if (ExeChanged != null && other.ExeChanged != null) return Math.Abs((ExeChanged.Value - other.ExeChanged.Value).TotalMinutes) > 1;
        return false;
    }
}

/// <summary>A mod that may not survive the game's update, and why.</summary>
public sealed record RiskyMod(string ModId, string Name, string Why);

/// <param name="Updated">The game is a different build than the one this PC last saw.</param>
public sealed record GameUpdateWarning(bool Updated, GameBuild Before, GameBuild Now, List<RiskyMod> Risky)
{
    /// <summary>The line the window shows, or null when there is nothing to say.</summary>
    public string? Note => !Updated ? null
        : $"7 Days to Die was updated ({Before.Describe} to {Now.Describe})."
          + (Risky.Count == 0
              ? " None of your enabled mods look likely to break, but check them if the game misbehaves."
              : $" {Risky.Count} enabled mod{(Risky.Count == 1 ? "" : "s")} may break: "
                + string.Join(", ", Risky.Take(4).Select(r => r.Name))
                + (Risky.Count > 4 ? $" and {Risky.Count - 4} more" : "")
                + ". Check for updates (Tools > Check mods for updates on GitHub).");
}

/// <summary>
/// The game update warning: 7 Days to Die updates break mods, above all the ones with code, which are built against
/// the game's own DLLs. The build this PC last saw is kept in the settings, so the app can say "the game changed, and
/// these mods are the ones to look at".
/// </summary>
public static class GameUpdate
{
    /// <summary>What is installed now: Steam's build id, the version from the newest log, and the exe's date.</summary>
    public static GameBuild Current(SteamLibraries steam, GameInstall install, ModAnalysis? analysis = null)
    {
        DateTime? exe = null;
        try
        {
            var path = Path.Combine(install.GameFolder, SevenDays.Exe);
            if (File.Exists(path)) exe = File.GetLastWriteTime(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var version = (analysis ?? ModAnalysis.For(install)).Game?.Text;
        return new GameBuild(steam.AppBuildId(SevenDays.AppId), version, exe);
    }

    /// <summary>
    /// Compares what is installed with what the settings remember, and lists the enabled mods to look at: mods with
    /// code first (the game's own DLLs changed under them), then mods that say they are for an older game.
    /// </summary>
    public static GameUpdateWarning Check(GameBuild? seen, GameBuild now, IEnumerable<InstalledMod> mods, ModAnalysis analysis)
    {
        var before = seen ?? new GameBuild();
        if (!before.Known || !now.Known || !now.DiffersFrom(before))
            return new GameUpdateWarning(false, before, now, new List<RiskyMod>());

        var risky = new List<RiskyMod>();
        foreach (var mod in mods.Where(m => m.Enabled && !m.Locked))
        {
            if (mod.HasCode)
                risky.Add(new RiskyMod(mod.Id, mod.DisplayName, "it has code, which is built against the game's own DLLs"));
            else if (mod.Info is { } info && ModAnalysis.MadeFor(info) is { } madeFor && ModAnalysis.IsOlder(madeFor, analysis.Game))
                risky.Add(new RiskyMod(mod.Id, mod.DisplayName, $"it says it is for {madeFor}"));
        }
        return new GameUpdateWarning(true, before, now, risky);
    }
}
