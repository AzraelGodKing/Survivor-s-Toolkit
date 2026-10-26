namespace SurvivorsToolkit.Core;

/// <summary>
/// What the last log says about how the game ended.
/// </summary>
/// <param name="LogPath">The log it read, or null when there is none.</param>
/// <param name="Crashed">The game ended badly: it said so, or the log just stops.</param>
/// <param name="Reason">The line that says so, or why we think the log stops short.</param>
/// <param name="Errors">The errors in the log, worst and earliest first, as the log viewer groups them.</param>
/// <param name="SuspectModId">The mod most of those errors point at, or null.</param>
/// <param name="SuspectWhy">How that mod was picked.</param>
/// <param name="Tail">The last lines of the log, for showing as they are.</param>
public sealed record CrashReport(string? LogPath, DateTime Written, bool Crashed, string? Reason,
    List<LogGroup> Errors, string? SuspectModId, string? SuspectWhy, List<string> Tail);

/// <summary>
/// The crash helper: after a crash, the last errors out of the game's log and the mod most likely behind them. It is
/// the log viewer's own grouping, read down to one answer - "it looks like this mod" - so nobody has to read a log.
/// </summary>
public static class Crash
{
    /// <summary>Lines that mean the game fell over rather than being closed.</summary>
    static readonly string[] CrashLines =
    {
        "Fatal error", "Crash!!!", "Unhandled Exception", "Unhandled exception", "StackOverflowException",
        "OutOfMemoryException", "Received signal", "Native stacktrace", "Obtained 1 stack frames",
        "Mono: Stacktrace", "Could not allocate memory", "Unity Player [version",
    };

    /// <summary>Lines near the end that mean the game was closed the normal way.</summary>
    static readonly string[] CleanLines = { "Shutdown", "Exiting", "ApplicationQuit", "Quit the game", "OnApplicationQuit", "Cleanup mod" };

    /// <summary>How many lines at the end count as "near the end", and how many the report keeps.</summary>
    public const int TailLines = 40;

    /// <summary>Reads the newest log and says how the game ended. <paramref name="mods"/> is used to blame one.</summary>
    public static CrashReport Check(GameInstall install, IReadOnlyList<InstalledMod> mods)
    {
        var path = SevenDays.LatestLog(install);
        if (path == null) return new CrashReport(null, default, false, "There is no log yet: the game hasn't run on this PC.", new List<LogGroup>(), null, null, new List<string>());
        try
        {
            return Report(LogAnalysis.ReadLines(path), mods, path, File.GetLastWriteTime(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new CrashReport(path, default, false, "Couldn't read the log: " + e.Message, new List<LogGroup>(), null, null, new List<string>());
        }
    }

    /// <summary>The report for a log that has been read: the parsing, apart from the files, so it can be tested.</summary>
    public static CrashReport Report(IReadOnlyList<string> lines, IReadOnlyList<InstalledMod> mods, string path, DateTime written)
    {
        var groups = LogAnalysis.Analyze(lines, mods);
        var errors = groups.Where(g => g.Severity == Severity.Error).ToList();
        var tail = lines.Skip(Math.Max(0, lines.Count - TailLines)).ToList();

        var said = lines.Select((text, i) => (text, i)).LastOrDefault(l => CrashLines.Any(c => l.text.Contains(c, StringComparison.OrdinalIgnoreCase)));
        var crashed = said.text != null;
        var reason = crashed ? said.text!.Trim() : null;
        if (!crashed && lines.Count > 0 && !tail.Any(l => CleanLines.Any(c => l.Contains(c, StringComparison.OrdinalIgnoreCase))))
        {
            crashed = true;
            reason = "The log stops without the game shutting down, which is what a crash looks like.";
        }

        // The mod behind most of the errors, by how many times they came up.
        var suspect = errors.Where(g => g.ModId != null).GroupBy(g => g.ModId!)
            .OrderByDescending(g => g.Sum(x => x.Count)).ThenByDescending(g => g.Count()).FirstOrDefault();
        var why = suspect == null ? null : suspect.First().Why;
        return new CrashReport(path, written, crashed, reason, errors, suspect?.Key, why, tail);
    }

    /// <summary>A line or two for the window: what happened and what to look at.</summary>
    public static string Describe(CrashReport r, Func<string, string?> modName)
    {
        if (r.LogPath == null) return r.Reason ?? "There is no log to read.";
        var when = r.Written == default ? "" : $" ({r.Written:g})";
        if (!r.Crashed && r.Errors.Count == 0) return $"The last session{when} ended normally, with no errors in the log.";
        var start = r.Crashed ? $"The last session{when} ended badly." : $"The last session{when} ended normally, but the log has errors.";
        var errors = r.Errors.Count == 0 ? " No errors were logged, so the crash left nothing behind." : $" {r.Errors.Count} error{(r.Errors.Count == 1 ? "" : "s")} in the log.";
        var blame = r.SuspectModId is { } id && modName(id) is { } name
            ? $" Most of them point at {name} ({r.SuspectWhy})."
            : r.Errors.Count > 0 ? " None of them point at a mod of yours." : "";
        return start + errors + blame;
    }
}
