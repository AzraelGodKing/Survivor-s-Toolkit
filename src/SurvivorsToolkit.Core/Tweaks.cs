using System.Xml;
using System.Xml.Linq;

namespace SurvivorsToolkit.Core;

/// <summary>One thing in a config file that can be tweaked: an item, a block, an entity class, a recipe.</summary>
/// <param name="Element">Its element name ("item", "block"), as the xpath needs it.</param>
/// <param name="Name">Its name attribute ("gunPistol").</param>
/// <param name="Properties">Its &lt;property name= value=&gt; children: the name and what the game has now.</param>
/// <param name="Attributes">Its own attributes other than name.</param>
public sealed record TweakThing(string Element, string Name, List<(string Name, string Value)> Properties, List<(string Name, string Value)> Attributes);

/// <summary>One patch the builder will write: a value set in one config file.</summary>
/// <param name="File">The config file, as the mod's Config folder names it ("items.xml").</param>
/// <param name="Xpath">The xpath of the attribute it sets.</param>
/// <param name="Value">What to set it to.</param>
/// <param name="Describe">The line the window shows ("every item: Stacknumber 1000, was 500").</param>
public sealed record Tweak(string File, string Xpath, string Value, string Describe);

/// <summary>
/// The XML tweak builder: small XPath mods made from a form instead of by hand. The game's own config files are read
/// from Data/Config, so the things, their properties and the values they have now are the real ones, and the xpath is
/// built from the document rather than guessed. The result is an ordinary mod folder with a Config file in it.
/// </summary>
public static class Tweaks
{
    /// <summary>The config files the game ships, by name ("items.xml"), in alphabetical order.</summary>
    public static List<string> ConfigFiles(GameInstall install)
    {
        try
        {
            return Directory.Exists(install.ConfigFolder)
                ? new DirectoryInfo(install.ConfigFolder).GetFiles("*.xml").Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new List<string>(); }
    }

    /// <summary>Reads one of the game's config files. Throws <see cref="FormatException"/> when it isn't valid XML.</summary>
    public static List<TweakThing> Read(GameInstall install, string file) =>
        Things(File.ReadAllText(Path.Combine(install.ConfigFolder, file)));

    /// <summary>
    /// The named things in a config file: the children of the root that have a name attribute, with their properties
    /// and attributes. Throws <see cref="FormatException"/> when the file isn't valid XML.
    /// </summary>
    public static List<TweakThing> Things(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml.TrimStart('﻿', ' ', '\t', '\r', '\n')); }
        catch (XmlException e) { throw new FormatException("That file isn't valid XML: " + e.Message, e); }
        var list = new List<TweakThing>();
        foreach (var e in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if ((string?)e.Attribute("name") is not { Length: > 0 } name) continue;
            var properties = e.Elements("property")
                .Where(p => (string?)p.Attribute("name") is { Length: > 0 })
                .Select(p => ((string)p.Attribute("name")!, (string?)p.Attribute("value") ?? ""))
                .ToList();
            var attributes = e.Attributes().Where(a => a.Name.LocalName != "name").Select(a => (a.Name.LocalName, a.Value)).ToList();
            list.Add(new TweakThing(e.Name.LocalName, name, properties, attributes));
        }
        return list;
    }

    /// <summary>The root element's name ("items"), which every xpath starts with.</summary>
    public static string Root(string xml)
    {
        try { return XDocument.Parse(xml.TrimStart('﻿', ' ', '\t', '\r', '\n')).Root?.Name.LocalName ?? ""; }
        catch (XmlException e) { throw new FormatException("That file isn't valid XML: " + e.Message, e); }
    }

    /// <summary>
    /// A tweak that sets one property of one thing, or - when <paramref name="thing"/> is null - of every thing of that
    /// kind in the file ("every item").
    /// </summary>
    public static Tweak SetProperty(string file, string root, string element, string? thing, string property, string value, string? was = null)
    {
        var where = thing == null ? $"/{root}/{element}" : $"/{root}/{element}[@name='{thing}']";
        var what = thing == null ? $"every {element}" : thing;
        return new Tweak(file, $"{where}/property[@name='{property}']/@value", value,
            $"{what}: {property} = {value}" + (was is { Length: > 0 } ? $" (was {was})" : ""));
    }

    /// <summary>A tweak that sets an attribute of a thing itself, such as an item's own count.</summary>
    public static Tweak SetAttribute(string file, string root, string element, string? thing, string attribute, string value, string? was = null)
    {
        var where = thing == null ? $"/{root}/{element}" : $"/{root}/{element}[@name='{thing}']";
        var what = thing == null ? $"every {element}" : thing;
        return new Tweak(file, $"{where}/@{attribute}", value,
            $"{what}: {attribute} = {value}" + (was is { Length: > 0 } ? $" (was {was})" : ""));
    }

    /// <summary>
    /// The Config file a mod needs for the tweaks of one game file: a &lt;config&gt; of &lt;set&gt; patches, the way the
    /// game's XML patching wants them.
    /// </summary>
    public static string Patch(string file, IEnumerable<Tweak> tweaks)
    {
        var doc = new XDocument(new XElement("config"));
        foreach (var t in tweaks.Where(t => string.Equals(t.File, file, StringComparison.OrdinalIgnoreCase)))
            doc.Root!.Add(new XComment(" " + t.Describe + " "), new XElement("set", new XAttribute("xpath", t.Xpath), t.Value));
        return """<?xml version="1.0" encoding="UTF-8" ?>""" + Environment.NewLine + doc.ToString();
    }

    /// <summary>The ModInfo.xml of the mod the builder writes.</summary>
    public static string ModInfoXml(string name, string version, string author, string description) =>
        $"""
        <?xml version="1.0" encoding="UTF-8" ?>
        <xml>
            <Name value="{Escape(name)}" />
            <DisplayName value="{Escape(name)}" />
            <Version value="{Escape(version)}" />
            <Author value="{Escape(author)}" />
            <Description value="{Escape(description)}" />
        </xml>
        """;

    static string Escape(string text) => text.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// Writes the mod: &lt;mods&gt;\&lt;folder&gt;\ModInfo.xml and a Config file for each game file tweaked. Throws
    /// <see cref="IOException"/> when the folder is already there. Returns the mod's folder.
    /// </summary>
    public static string Build(string modsFolder, string name, string version, string author, IReadOnlyList<Tweak> tweaks)
    {
        if (tweaks.Count == 0) throw new ArgumentException("Add at least one tweak first.");
        var folder = Path.Combine(modsFolder, Backups.Safe(name));
        if (Directory.Exists(folder)) throw new IOException($"There is already a mod folder called \"{Path.GetFileName(folder)}\". Pick another name.");
        var files = tweaks.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var description = $"{tweaks.Count} tweak{(tweaks.Count == 1 ? "" : "s")} to {string.Join(", ", files)}, made with Survivor's Toolkit.";
        Directory.CreateDirectory(Path.Combine(folder, "Config"));
        File.WriteAllText(Path.Combine(folder, ModInfo.FileName), ModInfoXml(name, version, author, description));
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, "Config", file), Patch(file, tweaks));
        return folder;
    }
}
