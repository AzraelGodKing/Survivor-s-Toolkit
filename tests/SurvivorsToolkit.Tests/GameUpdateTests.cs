namespace SurvivorsToolkit.Tests;

public class GameUpdateTests
{
    static readonly ModAnalysis OneDotFour = new(new GameVersion("V 1.4 (b8)", false, 1, 4));

    [Fact]
    public void ANewSteamBuildIdIsAnUpdate() =>
        Assert.True(new GameBuild("222", "V 1.4 (b8)").DiffersFrom(new GameBuild("111", "V 1.4 (b8)")));

    [Fact]
    public void TheSameBuildIsNotAnUpdate() =>
        Assert.False(new GameBuild("111", "V 1.4 (b8)").DiffersFrom(new GameBuild("111", "V 1.4 (b8)")));

    [Fact]
    public void WhatNeitherSideKnowsIsNotCompared()
    {
        // No Steam on either side: the versions decide.
        Assert.True(new GameBuild(null, "V 2.0").DiffersFrom(new GameBuild(null, "V 1.4")));
        // Nothing in common to compare, so no update is claimed.
        Assert.False(new GameBuild("111").DiffersFrom(new GameBuild(null, "V 1.4")));
    }

    [Fact]
    public void NothingIsSaidOnAFirstRun()
    {
        var w = GameUpdate.Check(null, new GameBuild("111", "V 1.4"), new List<InstalledMod>(), OneDotFour);
        Assert.False(w.Updated);
        Assert.Null(w.Note);
    }

    [Fact]
    public void NamesTheCodeModsAndTheOnesMadeForAnOlderGame()
    {
        using var g = new FakeGame();
        g.AddMod(g.Install.GameMods, "CodeMod", "CodeMod", dll: true);
        g.AddMod(g.Install.GameMods, "XmlMod", "XmlMod");
        var alpha = g.AddMod(g.Install.GameMods, "OldMod", "OldMod");
        File.WriteAllText(Path.Combine(alpha, ModInfo.FileName), FakeGame.ModInfoXml("OldMod").Replace("Does things", "For Alpha 20"));
        var mods = ModLibrary.Scan(g.Install).Mods;

        var w = GameUpdate.Check(new GameBuild("111", "V 1.3"), new GameBuild("222", "V 1.4"), mods, OneDotFour);
        Assert.True(w.Updated);
        Assert.Equal(new[] { "CodeMod", "OldMod" }, w.Risky.Select(r => r.Name).OrderBy(n => n));
        Assert.Contains("it has code", w.Risky.Single(r => r.Name == "CodeMod").Why);
        Assert.Contains("Alpha 20", w.Risky.Single(r => r.Name == "OldMod").Why);
        Assert.Contains("V 1.3 to V 1.4", w.Note);
        Assert.Contains("2 enabled mods may break", w.Note);
    }

    [Fact]
    public void TheGamesOwnModsAreNeverBlamed()
    {
        using var g = new FakeGame();
        var mods = ModLibrary.Scan(g.Install).Mods;
        Assert.All(mods, m => Assert.True(m.Locked));
        var w = GameUpdate.Check(new GameBuild("111"), new GameBuild("222"), mods, OneDotFour);
        Assert.True(w.Updated);
        Assert.Empty(w.Risky);
        Assert.Contains("None of your enabled mods", w.Note);
    }

    [Fact]
    public void DisabledModsAreLeftOutOfTheWarning()
    {
        using var g = new FakeGame();
        g.AddMod(g.Disabled(ModLocation.Game), "CodeMod", "CodeMod", dll: true);
        var mods = ModLibrary.Scan(g.Install).Mods;
        var w = GameUpdate.Check(new GameBuild("111"), new GameBuild("222"), mods, OneDotFour);
        Assert.Empty(w.Risky);
    }

    [Fact]
    public void ReadsSteamsBuildIdForTheGame()
    {
        using var g = new FakeGame();
        var apps = Path.Combine(g.Root, "steam", "steamapps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, $"appmanifest_{SevenDays.AppId}.acf"), """
            "AppState"
            {
            	"appid"		"251570"
            	"buildid"		"18274619"
            	"installdir"		"7 Days To Die"
            }
            """);
        var steam = new SteamLibraries(Path.Combine(g.Root, "steam"), new[] { Path.Combine(g.Root, "steam") });
        Assert.Equal("18274619", steam.AppBuildId(SevenDays.AppId));
        Assert.Null(steam.AppBuildId("999999"));
    }
}
