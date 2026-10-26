using System.IO.Compression;

namespace SurvivorsToolkit.Core;

/// <summary>A world in Saves, with the games saved in it.</summary>
/// <param name="Generated">The folder in GeneratedWorlds that holds this world's map, when there is one (random gen worlds only).</param>
public sealed record World(string Name, string Folder, List<SaveGame> Saves, string? Generated, long GeneratedSize)
{
    public long Size => Saves.Sum(s => s.Size) + GeneratedSize;
    public DateTime? LastPlayed => Saves.Count == 0 ? null : Saves.Max(s => s.LastPlayed);
}

/// <summary>
/// The worlds and saves in the player's data folder: Saves\&lt;World&gt;\&lt;Save&gt;, and the maps the game builds for
/// random gen worlds in GeneratedWorlds\&lt;World&gt;. Renaming, deleting and restoring them, each with a backup first.
/// </summary>
public static class Worlds
{
    public const string GeneratedFolderName = "GeneratedWorlds";

    /// <summary>GeneratedWorlds in the player's data folder.</summary>
    public static string GeneratedFolder(GameInstall install) => Path.Combine(install.UserDataFolder, GeneratedFolderName);

    /// <summary>Every world with its saves, the most recently played first; worlds with no saves last.</summary>
    public static List<World> List(GameInstall install)
    {
        var saves = Saves.List(install).GroupBy(s => s.World, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastPlayed).ToList(), StringComparer.OrdinalIgnoreCase);
        var list = new List<World>();
        foreach (var name in Names(install.Saves).Concat(Names(GeneratedFolder(install))).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var generated = Path.Combine(GeneratedFolder(install), name);
            var hasMap = Directory.Exists(generated);
            list.Add(new World(name, Path.Combine(install.Saves, name), saves.GetValueOrDefault(name, new List<SaveGame>()),
                hasMap ? generated : null, hasMap ? FolderSize(generated) : 0));
        }
        return list.OrderByDescending(w => w.LastPlayed ?? DateTime.MinValue).ToList();
    }

    static IEnumerable<string> Names(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.GetDirectories(folder).Select(d => Path.GetFileName(d) ?? "").Where(n => n.Length > 0).ToList()
                : Enumerable.Empty<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
    }

    /// <summary>What everything in a folder adds up to; 0 when it can't be read.</summary>
    public static long FolderSize(string folder)
    {
        try
        {
            return new DirectoryInfo(folder)
                .EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).Sum(f => f.Length);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>
    /// A name a save or world folder can have: no path, no empty name, nothing Windows refuses. Throws
    /// <see cref="ArgumentException"/> when it isn't one.
    /// </summary>
    public static string CheckName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new ArgumentException("Give it a name.");
        if (n != Backups.Safe(n)) throw new ArgumentException(@"A name can't hold \ / : * ? "" < > | or end with a dot.");
        return n;
    }

    /// <summary>
    /// Renames a save (the folder under its world). The game names the save inside its own files too, so the new name
    /// is what the game shows the next time it reads the folder. Returns the new folder.
    /// </summary>
    public static string RenameSave(SaveGame save, string newName)
    {
        var name = CheckName(newName);
        if (string.Equals(name, save.Name, StringComparison.Ordinal)) return save.Folder;
        var dest = Path.Combine(Path.GetDirectoryName(save.Folder)!, name);
        if (Directory.Exists(dest)) throw new IOException($"There is already a save called \"{name}\" in {save.World}.");
        Backups.MoveFolder(save.Folder, dest);
        return dest;
    }

    /// <summary>
    /// Renames a world: its Saves folder and its GeneratedWorlds folder together, so the saves in it still find their
    /// map. Returns the new Saves folder.
    /// </summary>
    public static string RenameWorld(World world, string newName)
    {
        var name = CheckName(newName);
        if (string.Equals(name, world.Name, StringComparison.Ordinal)) return world.Folder;
        var dest = Path.Combine(Path.GetDirectoryName(world.Folder)!, name);
        if (Directory.Exists(dest)) throw new IOException($"There is already a world called \"{name}\".");
        if (Directory.Exists(world.Folder)) Backups.MoveFolder(world.Folder, dest);
        if (world.Generated != null)
        {
            var mapDest = Path.Combine(Path.GetDirectoryName(world.Generated)!, name);
            if (!Directory.Exists(mapDest)) Backups.MoveFolder(world.Generated, mapDest);
        }
        return dest;
    }

    /// <summary>Backs a save up to a zip and deletes it. Returns the zip, or null when it wasn't backed up.</summary>
    public static string? DeleteSave(SaveGame save, string backupFolder, bool backupFirst = true)
    {
        var zip = backupFirst ? Saves.Backup(save, backupFolder) : null;
        Directory.Delete(save.Folder, true);
        return zip;
    }

    /// <summary>
    /// Deletes a world: its saves (each backed up first) and, when <paramref name="withMap"/>, the map in
    /// GeneratedWorlds. Returns the backups made. A random gen world's map is built again by the game when it is
    /// needed, so dropping it is a way to get the space back.
    /// </summary>
    public static List<string> DeleteWorld(World world, string backupFolder, bool withMap, bool backupFirst = true)
    {
        var zips = new List<string>();
        foreach (var save in world.Saves)
            if (DeleteSave(save, backupFolder, backupFirst) is { } zip) zips.Add(zip);
        if (Directory.Exists(world.Folder)) Directory.Delete(world.Folder, true);
        if (withMap && world.Generated != null && Directory.Exists(world.Generated)) Directory.Delete(world.Generated, true);
        return zips;
    }

    /// <summary>Deletes only a world's map in GeneratedWorlds, keeping its saves. The game builds the map again from the world's seed.</summary>
    public static void DeleteMap(World world)
    {
        if (world.Generated != null && Directory.Exists(world.Generated)) Directory.Delete(world.Generated, true);
    }

    /// <summary>
    /// Puts a backup zip back as a save in <paramref name="world"/>. The zip holds the save's own folder, so the save
    /// keeps the name it had; <paramref name="asName"/> renames it. What it replaces is backed up first. Returns the
    /// restored folder.
    /// </summary>
    public static string RestoreSave(string zip, string worldFolder, string backupFolder, string? asName = null)
    {
        using var archive = ZipFile.OpenRead(zip);
        var root = archive.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).FirstOrDefault(n => n.Length > 0)
            ?? throw new InvalidDataException("That zip is empty.");
        var name = CheckName(asName ?? root);
        var dest = Path.Combine(worldFolder, name);
        if (Directory.Exists(dest))
        {
            var existing = new SaveGame(Path.GetFileName(worldFolder), name, dest, Directory.GetLastWriteTime(dest), FolderSize(dest));
            Saves.Backup(existing, backupFolder);
            Directory.Delete(dest, true);
        }
        Directory.CreateDirectory(worldFolder);
        // The zip holds <root>/..., so it unpacks beside the world's other saves.
        var temp = Path.Combine(worldFolder, "." + name + "-restoring");
        if (Directory.Exists(temp)) Directory.Delete(temp, true);
        try
        {
            ZipFile.ExtractToDirectory(zip, temp);
            var from = Path.Combine(temp, root);
            Backups.MoveFolder(Directory.Exists(from) ? from : temp, dest);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        return dest;
    }
}
