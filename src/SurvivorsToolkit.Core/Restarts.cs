using System.Globalization;

namespace SurvivorsToolkit.Core;

/// <summary>One thing a restart does, and when.</summary>
public enum RestartStep
{
    /// <summary>Tell everyone in chat how long they have.</summary>
    Warn,
    /// <summary>saveworld, so nothing is lost.</summary>
    SaveWorld,
    /// <summary>shutdown: the server saves and closes.</summary>
    Shutdown,
    /// <summary>Start the server on this PC again (a hosted server is started by the host).</summary>
    StartAgain,
    /// <summary>Say in chat that the server is back.</summary>
    Back,
}

/// <param name="When">When it happens.</param>
/// <param name="Command">The console command, or null for <see cref="RestartStep.StartAgain"/>.</param>
/// <param name="Describe">The line the window and the command line show.</param>
public sealed record RestartAction(DateTime When, RestartStep Step, string? Command, string Describe);

/// <summary>
/// A restart that warns the players first: "the server restarts in 15 minutes" in chat, again at 5 and 1, then
/// saveworld, shutdown, and - for a server on this PC - start it again and say it is back. Kept in the settings.
/// </summary>
public sealed class RestartPlan
{
    /// <summary>Run the restarts while the app is open.</summary>
    public bool Enabled { get; set; }
    /// <summary>Times of day, as "HH:mm" (the PC's clock): usually one quiet hour, sometimes a few.</summary>
    public List<string> Times { get; set; } = new() { "05:00" };
    /// <summary>How many minutes before the restart to warn, largest first.</summary>
    public List<int> WarnMinutes { get; set; } = new() { 15, 5, 1 };
    /// <summary>What the warning says; {minutes} is how many are left and {time} the restart's time.</summary>
    public string Message { get; set; } = "The server restarts in {minutes} minutes. Get somewhere safe.";
    /// <summary>What it says once the server is back.</summary>
    public string BackMessage { get; set; } = "The server is back up. Welcome back, survivors.";
    /// <summary>saveworld before the shutdown.</summary>
    public bool SaveWorld { get; set; } = true;
    /// <summary>Start the server on this PC again afterwards. A hosted server is started by the host, so leave it off.</summary>
    public bool StartAgain { get; set; }
    /// <summary>How long to wait after the shutdown before starting it again.</summary>
    public int StartAgainAfterSeconds { get; set; } = 30;
    /// <summary>How long to give the server to come up before saying it is back; 0 says nothing.</summary>
    public int BackAfterSeconds { get; set; } = 120;

    /// <summary>The times that could be read, as times of day, in order.</summary>
    public List<TimeSpan> ReadTimes() =>
        Times.Select(Restarts.ReadTime).Where(t => t != null).Select(t => t!.Value).Distinct().OrderBy(t => t).ToList();

    /// <summary>The next restart after <paramref name="now"/>, or null when no time could be read.</summary>
    public DateTime? NextRun(DateTime now) => Restarts.NextRun(ReadTimes(), now);

    /// <summary>Everything the restart at <paramref name="runAt"/> does, in order.</summary>
    public List<RestartAction> Timeline(DateTime runAt) => Restarts.Timeline(this, runAt);
}

/// <summary>The restart arithmetic, apart from the settings so it can be tested with a clock of our own.</summary>
public static class Restarts
{
    /// <summary>"05:00", "5:00", "05.00" as a time of day; null when it isn't one.</summary>
    public static TimeSpan? ReadTime(string? text)
    {
        var t = (text ?? "").Trim().Replace('.', ':');
        return TimeSpan.TryParseExact(t, new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" }, CultureInfo.InvariantCulture, out var v)
            && v >= TimeSpan.Zero && v < TimeSpan.FromDays(1) ? v : null;
    }

    /// <summary>The first of <paramref name="times"/> after <paramref name="now"/>, today or tomorrow.</summary>
    public static DateTime? NextRun(IReadOnlyList<TimeSpan> times, DateTime now)
    {
        if (times.Count == 0) return null;
        var today = now.Date;
        return times.Select(t => today + t).Where(d => d > now)
            .Concat(times.Select(t => today.AddDays(1) + t)).First();
    }

    /// <summary>Everything a restart does, in order: the warnings, the save, the shutdown, and coming back.</summary>
    public static List<RestartAction> Timeline(RestartPlan plan, DateTime runAt)
    {
        var steps = new List<RestartAction>();
        foreach (var minutes in plan.WarnMinutes.Where(m => m > 0).Distinct().OrderByDescending(m => m))
        {
            var message = Warning(plan.Message, minutes, runAt);
            steps.Add(new RestartAction(runAt.AddMinutes(-minutes), RestartStep.Warn, ServerCommands.Say(message), $"Say: {message}"));
        }
        if (plan.SaveWorld) steps.Add(new RestartAction(runAt, RestartStep.SaveWorld, "saveworld", "Save the world"));
        steps.Add(new RestartAction(runAt, RestartStep.Shutdown, "shutdown", "Shut the server down"));
        if (!plan.StartAgain) return steps;
        var startAt = runAt.AddSeconds(Math.Max(0, plan.StartAgainAfterSeconds));
        steps.Add(new RestartAction(startAt, RestartStep.StartAgain, null, "Start the server again"));
        if (plan.BackAfterSeconds > 0 && plan.BackMessage.Trim().Length > 0)
            steps.Add(new RestartAction(startAt.AddSeconds(plan.BackAfterSeconds), RestartStep.Back, ServerCommands.Say(plan.BackMessage.Trim()), $"Say: {plan.BackMessage.Trim()}"));
        return steps;
    }

    /// <summary>The warning with {minutes} and {time} filled in; "1 minute" reads properly.</summary>
    public static string Warning(string message, int minutes, DateTime runAt)
    {
        var text = (message.Trim().Length > 0 ? message : new RestartPlan().Message)
            .Replace("{minutes}", minutes.ToString())
            .Replace("{time}", runAt.ToString("HH:mm"));
        return minutes == 1 ? text.Replace("1 minutes", "1 minute") : text;
    }

    /// <summary>
    /// Waits for each step's time and does it. <paramref name="wait"/> and <paramref name="now"/> are passed in so a
    /// test can run a whole restart in no time at all; a step whose time has already gone by is done at once.
    /// </summary>
    public static async Task RunAsync(IEnumerable<RestartAction> steps, Func<RestartAction, CancellationToken, Task> doIt,
        Func<DateTime> now, Func<TimeSpan, CancellationToken, Task> wait, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            var left = step.When - now();
            if (left > TimeSpan.Zero) await wait(left, ct);
            ct.ThrowIfCancellationRequested();
            await doIt(step, ct);
        }
    }
}
