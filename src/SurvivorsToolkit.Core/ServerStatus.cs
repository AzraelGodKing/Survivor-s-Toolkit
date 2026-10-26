using System.Text.RegularExpressions;

namespace SurvivorsToolkit.Core;

/// <summary>One player on the server, as the console's "lp" lists them.</summary>
public sealed record PlayerOnServer(int Id, string Name, int? Health, int? Deaths, int? Zombies, int? Level, int? Ping, string? SteamId);

/// <summary>What the server is doing right now: its clock, who is on, the next blood moon and the game's version.</summary>
public sealed record ServerStatus(
    int? Day, int? Hour, int? Minute, int Players, string? Version, int? BloodMoonDay, List<PlayerOnServer> PlayerList)
{
    /// <summary>"Day 12, 08:34", or "?" when the console didn't say.</summary>
    public string Clock => Day == null ? "?" : $"Day {Day}, {Hour:00}:{Minute:00}";

    /// <summary>"tonight", "tomorrow night" or "in 4 days (day 14)": when the next blood moon starts.</summary>
    public string BloodMoon
    {
        get
        {
            if (Day == null || BloodMoonDay == null) return "?";
            var days = BloodMoonDay.Value - Day.Value;
            // The horde comes at 22:00, so on the day itself it is either tonight or already here.
            if (days == 0) return Hour >= 22 ? "now (day " + BloodMoonDay + ")" : "tonight (day " + BloodMoonDay + ", 22:00)";
            return days == 1 ? $"tomorrow night (day {BloodMoonDay})" : $"in {days} days (day {BloodMoonDay})";
        }
    }
}

/// <summary>
/// Reading the server's own answers. Every parse is on text, so it can be tested without a server: the console's
/// "gettime", "lp" and "version" are all the status panel needs.
/// </summary>
public static class ServerStatusReader
{
    static readonly Regex TimeLine = new(@"Day\s+(\d+),\s*(\d+):(\d+)", RegexOptions.Compiled);
    static readonly Regex PlayerLine = new(@"^\s*\d+\.\s*id=(\d+),\s*(.*?),\s*pos=", RegexOptions.Compiled | RegexOptions.Multiline);
    static readonly Regex TotalLine = new(@"Total of (\d+) in the game", RegexOptions.Compiled);
    static readonly Regex VersionLine = new(@"Game version:\s*(.+?)(?:\s+Compatibility|$)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The commands the status panel sends, in order.</summary>
    public static readonly string[] StatusCommands = { "gettime", "lp", "version" };

    /// <summary>Asks the console for the status. <paramref name="bloodMoonFrequency"/> is BloodMoonFrequency from the server's config.</summary>
    public static async Task<ServerStatus> QueryAsync(IServerConsole console, int bloodMoonFrequency, CancellationToken ct = default)
    {
        var time = await console.SendAsync("gettime", ct);
        var players = await console.SendAsync("lp", ct);
        var version = await console.SendAsync("version", ct);
        return Parse(time, players, version, bloodMoonFrequency);
    }

    /// <summary>Builds the status out of what "gettime", "lp" and "version" printed.</summary>
    public static ServerStatus Parse(string time, string players, string version, int bloodMoonFrequency)
    {
        var (day, hour, minute) = ParseTime(time);
        var list = ParsePlayers(players);
        var count = ParseTotal(players) ?? list.Count;
        return new ServerStatus(day, hour, minute, count, ParseVersion(version), NextBloodMoonDay(day, bloodMoonFrequency), list);
    }

    /// <summary>The day and clock out of "Day 12, 08:34"; nulls when it isn't there.</summary>
    public static (int? Day, int? Hour, int? Minute) ParseTime(string text)
    {
        var m = TimeLine.Match(text ?? "");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : (null, null, null);
    }

    /// <summary>The players out of "lp": one per numbered line, the rest of the line read as key=value pairs.</summary>
    public static List<PlayerOnServer> ParsePlayers(string text)
    {
        var list = new List<PlayerOnServer>();
        foreach (var line in (text ?? "").Split('\n'))
        {
            var m = PlayerLine.Match(line);
            if (!m.Success) continue;
            var fields = Fields(line);
            list.Add(new PlayerOnServer(int.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(),
                Number(fields, "health"), Number(fields, "deaths"), Number(fields, "zombies"), Number(fields, "level"), Number(fields, "ping"),
                fields.TryGetValue("steamid", out var steam) ? steam : null));
        }
        return list;
    }

    /// <summary>The count out of "Total of 2 in the game", or null when that line isn't there.</summary>
    public static int? ParseTotal(string text) =>
        TotalLine.Match(text ?? "") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>The game's version out of "Game version: V 1.0 (b333) Compatibility Version: V 1.0".</summary>
    public static string? ParseVersion(string text) =>
        VersionLine.Match(text ?? "") is { Success: true } m ? m.Groups[1].Value.Trim() : null;

    /// <summary>
    /// The day of the next blood moon: they come on every day that divides by <paramref name="frequency"/>, so on such
    /// a day it is that day itself (the horde comes at 22:00). Null when the day or the frequency isn't known.
    /// </summary>
    public static int? NextBloodMoonDay(int? day, int frequency) =>
        day is not { } d || d < 1 || frequency < 1 ? null : d % frequency == 0 ? d : (d / frequency + 1) * frequency;

    /// <summary>The "name=value, name=value" pairs on a line, lower-cased names.</summary>
    static Dictionary<string, string> Fields(string line)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in line.Split(','))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            var name = part[..eq].Trim();
            // pos=(1.2 and rot=(3.4 split across commas; only simple values are wanted.
            if (name.Length == 0 || name.Contains(' ')) continue;
            d[name] = part[(eq + 1)..].Trim();
        }
        return d;
    }

    static int? Number(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var v) && int.TryParse(v.Split('.')[0], out var n) ? n : null;
}

/// <summary>The console commands the admin console offers as buttons, with what they do.</summary>
public static class ServerCommands
{
    /// <summary>A "say" with the message quoted the way the console wants it.</summary>
    public static string Say(string message) => "say \"" + message.Replace("\"", "'") + "\"";

    /// <summary>Kicks a player by name or id, with a reason they see.</summary>
    public static string Kick(string player, string? reason) =>
        $"kick {Who(player)}" + (string.IsNullOrWhiteSpace(reason) ? "" : " \"" + reason.Trim().Replace("\"", "'") + "\"");

    /// <summary>Bans a player for a while ("1 day", "30 minutes"), with a reason.</summary>
    public static string Ban(string player, string duration, string? reason) =>
        $"ban add {Who(player)} {duration.Trim()}" + (string.IsNullOrWhiteSpace(reason) ? "" : " \"" + reason.Trim().Replace("\"", "'") + "\"");

    /// <summary>A player's id as it is, else the name in quotes.</summary>
    static string Who(string player) =>
        int.TryParse(player.Trim(), out _) || player.StartsWith("Steam_", StringComparison.OrdinalIgnoreCase) ? player.Trim() : "\"" + player.Trim().Replace("\"", "") + "\"";

    /// <summary>Commands that can't be taken back, so the window asks first.</summary>
    public static bool NeedsConfirming(string command)
    {
        var first = command.Trim().Split(' ', '\t')[0].ToLowerInvariant();
        return first is "shutdown" or "ban" or "kick" or "kickall" or "settempunit" or "deletearea" or "wipe";
    }
}
