namespace SurvivorsToolkit.Tests;

public class WorldTests
{
    /// <summary>Makes Saves\world\save with a file in it, and optionally a map in GeneratedWorlds\world.</summary>
    static void AddSave(FakeGame g, string world, string save, int bytes = 100, int mapBytes = 0)
    {
        var folder = Path.Combine(g.Install.Saves, world, save);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "main.ttw"), new byte[bytes]);
        if (mapBytes <= 0) return;
        var map = Path.Combine(Worlds.GeneratedFolder(g.Install), world);
        Directory.CreateDirectory(map);
        File.WriteAllBytes(Path.Combine(map, "dtm.raw"), new byte[mapBytes]);
    }

    [Fact]
    public void ListsWorldsWithTheirSavesAndMap()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one", 100);
        AddSave(g, "Pille County", "Solo", 200, mapBytes: 1000);
        AddSave(g, "Pille County", "With friends", 300);
        var worlds = Worlds.List(g.Install);
        Assert.Equal(2, worlds.Count);
        var pille = worlds.Single(w => w.Name == "Pille County");
        Assert.Equal(2, pille.Saves.Count);
        Assert.Equal(1000, pille.GeneratedSize);
        Assert.Equal(1500, pille.Size);
        Assert.NotNull(pille.Generated);
        Assert.Null(worlds.Single(w => w.Name == "Navezgane").Generated);
    }

    [Fact]
    public void AWorldWithAMapAndNoSavesIsStillListed()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(Path.Combine(Worlds.GeneratedFolder(g.Install), "Half built"));
        var world = Assert.Single(Worlds.List(g.Install));
        Assert.Equal("Half built", world.Name);
        Assert.Empty(world.Saves);
        Assert.Null(world.LastPlayed);
    }

    [Fact]
    public void RenamesASave()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one");
        var save = Saves.List(g.Install).Single();
        var folder = Worlds.RenameSave(save, "Day two");
        Assert.True(File.Exists(Path.Combine(folder, "main.ttw")));
        Assert.Equal("Day two", Saves.List(g.Install).Single().Name);
    }

    [Fact]
    public void WontRenameOverAnotherSave()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one");
        AddSave(g, "Navezgane", "Day two");
        var save = Saves.List(g.Install).First(s => s.Name == "Day one");
        Assert.Throws<IOException>(() => Worlds.RenameSave(save, "Day two"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(@"..\elsewhere")]
    [InlineData("has/slash")]
    public void WontTakeANameThatIsntOne(string name)
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one");
        var save = Saves.List(g.Install).Single();
        Assert.Throws<ArgumentException>(() => Worlds.RenameSave(save, name));
    }

    [Fact]
    public void RenamingAWorldTakesItsMapWithIt()
    {
        using var g = new FakeGame();
        AddSave(g, "Pille County", "Solo", mapBytes: 500);
        var world = Worlds.List(g.Install).Single();
        Worlds.RenameWorld(world, "Pille County 2");
        var renamed = Worlds.List(g.Install).Single();
        Assert.Equal("Pille County 2", renamed.Name);
        Assert.Equal(500, renamed.GeneratedSize);
        Assert.Single(renamed.Saves);
        Assert.False(Directory.Exists(Path.Combine(Worlds.GeneratedFolder(g.Install), "Pille County")));
    }

    [Fact]
    public void DeletingASaveBacksItUpFirst()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one");
        var save = Saves.List(g.Install).Single();
        var zip = Worlds.DeleteSave(save, g.Backups);
        Assert.NotNull(zip);
        Assert.True(File.Exists(zip));
        Assert.False(Directory.Exists(save.Folder));
    }

    [Fact]
    public void DeletingTheMapKeepsTheSaves()
    {
        using var g = new FakeGame();
        AddSave(g, "Pille County", "Solo", mapBytes: 500);
        Worlds.DeleteMap(Worlds.List(g.Install).Single());
        var world = Worlds.List(g.Install).Single();
        Assert.Null(world.Generated);
        Assert.Single(world.Saves);
    }

    [Fact]
    public void DeletingAWorldBacksUpEverySaveAndTakesTheMap()
    {
        using var g = new FakeGame();
        AddSave(g, "Pille County", "Solo", mapBytes: 500);
        AddSave(g, "Pille County", "With friends");
        var zips = Worlds.DeleteWorld(Worlds.List(g.Install).Single(), g.Backups, withMap: true);
        Assert.Equal(2, zips.Count);
        Assert.All(zips, z => Assert.True(File.Exists(z)));
        Assert.Empty(Worlds.List(g.Install));
    }

    [Fact]
    public void RestoresASaveFromItsBackup()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one", 100);
        var save = Saves.List(g.Install).Single();
        var zip = Saves.Backup(save, g.Backups);
        Worlds.DeleteSave(save, g.Backups, backupFirst: false);

        var folder = Worlds.RestoreSave(zip, Path.Combine(g.Install.Saves, "Navezgane"), g.Backups);
        Assert.Equal(save.Folder, folder);
        Assert.True(File.Exists(Path.Combine(folder, "main.ttw")));
        Assert.Equal("Day one", Saves.List(g.Install).Single().Name);
    }

    [Fact]
    public void RestoringOverASaveBacksUpWhatItReplaces()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one", 100);
        var zip = Saves.Backup(Saves.List(g.Install).Single(), g.Backups);
        AddSave(g, "Navezgane", "Day one", 999);

        Worlds.RestoreSave(zip, Path.Combine(g.Install.Saves, "Navezgane"), g.Backups);
        Assert.Equal(100, Saves.List(g.Install).Single().Size);
        // The one it replaced is in the backups too, beside the one restored from.
        Assert.Equal(2, Directory.GetFiles(g.Backups, "*.zip").Length);
    }

    [Fact]
    public void RestoresUnderAnotherName()
    {
        using var g = new FakeGame();
        AddSave(g, "Navezgane", "Day one");
        var zip = Saves.Backup(Saves.List(g.Install).Single(), g.Backups);
        Worlds.RestoreSave(zip, Path.Combine(g.Install.Saves, "Navezgane"), g.Backups, "A copy");
        Assert.Equal(new[] { "A copy", "Day one" }, Saves.List(g.Install).Select(s => s.Name).OrderBy(n => n));
    }
}

public class CrashTests
{
    const string Log = """
        2026-10-26T20:47:41 21.590 INF [MODS] Loaded Mod: SomeMod (1.0)
        2026-10-26T20:47:42 22.100 ERR XML loader: Patching 'blocks.xml' from mod 'SomeMod' failed
        NullReferenceException: Object reference not set
        2026-10-26T20:47:43 23.000 ERR XML loader: Patching 'items.xml' from mod 'SomeMod' failed
        NullReferenceException: Object reference not set
        2026-10-26T20:47:44 24.000 WRN Something small
        Unity Player [version: Unity 2022.3.29f1]
        Fatal error in GC
        """;

    static List<string> Lines(string text) => text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    [Fact]
    public void SpotsACrashAndBlamesTheModTheErrorsName()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "SomeMod", "SomeMod");
        var mods = ModLibrary.Scan(g.Install).Mods;
        var r = Crash.Report(Lines(Log), mods, "output_log.txt", new DateTime(2026, 10, 26, 20, 48, 0));
        Assert.True(r.Crashed);
        Assert.Contains("Fatal error", r.Reason);
        // The two patch errors are the same message with a different file in it, so they group.
        Assert.Equal(2, r.Errors.Sum(e => e.Count));
        Assert.Equal("game/SomeMod", r.SuspectModId);
        Assert.Contains("the message names it", r.SuspectWhy);
        Assert.Contains("SomeMod", Crash.Describe(r, id => id == "game/SomeMod" ? "SomeMod" : null));
    }

    [Fact]
    public void ALogThatJustStopsCountsAsACrash()
    {
        var lines = new List<string> { "2026-10-26T20:47:41 21.590 INF Loading world", "2026-10-26T20:47:42 22.000 INF Spawning player" };
        var r = Crash.Report(lines, new List<InstalledMod>(), "output_log.txt", DateTime.Now);
        Assert.True(r.Crashed);
        Assert.Contains("stops without the game shutting down", r.Reason);
    }

    [Fact]
    public void AGameThatWasClosedNormallyIsNotACrash()
    {
        var lines = new List<string> { "2026-10-26T20:47:41 21.590 INF Loading world", "2026-10-26T21:00:00 900.0 INF Shutdown" };
        var r = Crash.Report(lines, new List<InstalledMod>(), "output_log.txt", DateTime.Now);
        Assert.False(r.Crashed);
        Assert.Empty(r.Errors);
        Assert.Contains("ended normally", Crash.Describe(r, _ => null));
    }

    [Fact]
    public void SaysSoWhenNothingPointsAtAMod()
    {
        var lines = new List<string>
        {
            "2026-10-26T20:47:41 21.590 ERR Something broke deep in the game",
            "  at UnityEngine.Thing.Do ()",
            "Fatal error in GC",
        };
        var r = Crash.Report(lines, new List<InstalledMod>(), "output_log.txt", DateTime.Now);
        Assert.True(r.Crashed);
        Assert.Null(r.SuspectModId);
        Assert.Contains("None of them point at a mod", Crash.Describe(r, _ => null));
    }

    [Fact]
    public void KeepsTheEndOfTheLogToShow()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"line {i}").ToList();
        var r = Crash.Report(lines, new List<InstalledMod>(), "output_log.txt", DateTime.Now);
        Assert.Equal(Crash.TailLines, r.Tail.Count);
        Assert.Equal("line 200", r.Tail.Last());
    }
}
