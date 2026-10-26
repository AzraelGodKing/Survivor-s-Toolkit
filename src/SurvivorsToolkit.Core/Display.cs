namespace SurvivorsToolkit.Core;

/// <summary>Display choices that need no WinForms (the theme list and its names), so the tests can check them.</summary>
public static class Display
{
    public const string Dark = "Dark", Light = "Light", Kats = "Kats", Koneko = "Koneko", Serina = "Serina", Wolfie = "Wolfie", Vickra = "Vickra", Kitomi = "Kitomi", Szendoo = "Szendoo",
        Triphlx = "Triphlx", Tohma = "Tohma", Skywolf = "Skywolf", Hunter = "Hunter", System = "System",
        Wasteland = "Wasteland", WastelandLight = "WastelandLight";

    /// <summary>The theme a new install starts with: Wasteland, the 7 Days to Die look.</summary>
    public const string Default = Wasteland;

    /// <summary>The themes in Options > Appearance, in the drop-down's order: saved name and the name shown.</summary>
    public static readonly (string Name, string Label)[] Themes =
    {
        (Wasteland, "Wasteland (7 Days to Die, dark)"), (WastelandLight, "Wasteland Light (7 Days to Die, light)"),
        (Dark, "Dark"), (Light, "Light"), (Kats, "Kats (orange and gunmetal)"),
        (Koneko, "Koneko (purple and black)"), (Serina, "Serina (purple and mint green)"), (Wolfie, "Wolfie (blue and green)"),
        (Vickra, "Vickra (sapphire and emerald)"), (Kitomi, "Kitomi (red, black and cyan)"),
        (Szendoo, "Szendoo (grey and pink)"),
        (Triphlx, "Triphlx (black and blue)"), (Tohma, "Tohma (blue and red)"),
        (Skywolf, "Skywolf (deep purple and black)"), (Hunter, "Hunter (black and silver)"),
        (System, "Match Windows"),
    };

    /// <summary>The saved theme choice: one of <see cref="Themes"/> ("System" follows Windows). Anything else is <see cref="Default"/>.</summary>
    public static string NormalizeTheme(string? name) =>
        Themes.Select(t => t.Name).FirstOrDefault(t => string.Equals(name, t, StringComparison.OrdinalIgnoreCase)) ?? Default;

    /// <summary>
    /// The palette to use for a choice. "System" follows Windows' app mode (<paramref name="windowsAppsUseLight"/>, null
    /// when it could not be read, which counts as dark).
    /// </summary>
    public static string ResolveTheme(string? choice, bool? windowsAppsUseLight) => NormalizeTheme(choice) switch
    {
        System => windowsAppsUseLight == true ? Light : Dark,
        var c => c,
    };

    /// <summary>Text sizes in Options > Appearance, as factors.</summary>
    public static readonly float[] TextSizes = { 1f, 1.1f, 1.25f, 1.5f };

    /// <summary>A label's text without its "&amp;" access key marks, as screen readers should say it.</summary>
    public static string StripMnemonic(string text) => text.Replace("&&", "\0").Replace("&", "").Replace("\0", "&");

    /// <summary>How long until something, as people say it: "in 4 minutes", "in 2 hours 10 minutes", "now".</summary>
    public static string In(TimeSpan left)
    {
        if (left <= TimeSpan.Zero) return "now";
        if (left < TimeSpan.FromMinutes(1)) return "in less than a minute";
        var hours = (int)left.TotalHours;
        var minutes = left.Minutes;
        if (hours == 0) return $"in {minutes} minute{(minutes == 1 ? "" : "s")}";
        return $"in {hours} hour{(hours == 1 ? "" : "s")}" + (minutes > 0 ? $" {minutes} minute{(minutes == 1 ? "" : "s")}" : "");
    }

    /// <summary>A byte count as people read it: "512 B", "1.4 MB".</summary>
    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}
