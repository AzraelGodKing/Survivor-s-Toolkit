namespace SurvivorsToolkit.Tests;

public class UpdateCheckTests
{
    static string Release(string tag, bool draft = false, bool pre = false, string body = "notes") => $$"""
        { "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{pre.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/x/{{tag}}", "body": "{{body}}",
          "assets": [
            { "name": "SurvivorsToolkit-{{tag[1..]}}-win-x64.exe", "browser_download_url": "https://dl/{{tag}}/app.exe" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://dl/{{tag}}/SHA256SUMS.txt" } ] }
        """;

    [Fact]
    public void PicksTheNewestReleaseAndIgnoresOtherTags()
    {
        var json = "[" + string.Join(",",
            """{ "tag_name": "AzraelSpeedometer-v9.9.9", "assets": [] }""",
            """{ "tag_name": "nightly", "assets": [] }""",
            Release("v0.2.0"),
            Release("v0.10.0"),
            Release("v0.11.0", draft: true),
            Release("v0.12.0", pre: true)) + "]";
        var r = UpdateCheck.Newest(json);
        Assert.NotNull(r);
        Assert.Equal("0.10.0", r!.Version);
        Assert.Equal("https://dl/v0.10.0/app.exe", r.ExeUrl);
        Assert.Equal("https://dl/v0.10.0/SHA256SUMS.txt", r.ChecksumsUrl);
        Assert.Equal("notes", r.Notes);
    }

    [Fact]
    public void NoVersionTagMeansNoUpdate() =>
        Assert.Null(UpdateCheck.Newest("""[{ "tag_name": "HonkDoorFix-v1.0.0", "assets": [] }]"""));

    [Theory]
    [InlineData("0.10.0", "0.9.0", true)]
    [InlineData("1.0", "1.0.0", false)]
    [InlineData("0.1.0", "0.1.1", false)]
    [InlineData("junk", "0.1.0", false)]
    public void ComparesVersions(string latest, string current, bool newer) => Assert.Equal(newer, UpdateCheck.IsNewer(latest, current));
}

public class SelfUpdateTests
{
    [Fact]
    public void ReadsChecksumListings()
    {
        var hash = new string('a', 64);
        var d = SelfUpdate.ParseChecksums($"{hash}  SurvivorsToolkit-1.0.0-win-x64.exe\n{hash} *other.zip\nnot a line\n");
        Assert.Equal(hash, d["SurvivorsToolkit-1.0.0-win-x64.exe"]);
        Assert.Equal(hash, d["other.zip"]);
        Assert.Equal(2, d.Count);
    }

    [Fact]
    public void ApplyKeepsTheOldExeAndCleanUpRemovesIt()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var target = Path.Combine(g.Root, "SurvivorsToolkit.exe");
        var fresh = Path.Combine(g.Root, "new.exe");
        File.WriteAllText(target, "old");
        File.WriteAllText(fresh, "new");

        SelfUpdate.Apply(fresh, target);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.Equal("old", File.ReadAllText(target + SelfUpdate.OldSuffix));
        Assert.Equal(1, SelfUpdate.CleanUp(target, attempts: 1));
        Assert.False(File.Exists(target + SelfUpdate.OldSuffix));
    }

    [Fact]
    public void ApplyPutsTheOldExeBackWhenTheCopyFails()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var target = Path.Combine(g.Root, "SurvivorsToolkit.exe");
        File.WriteAllText(target, "old");
        Assert.ThrowsAny<IOException>(() => SelfUpdate.Apply(Path.Combine(g.Root, "missing.exe"), target));
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void CleanStagingKeepsOnlyNewerDownloads()
    {
        using var g = new FakeGame();
        foreach (var v in new[] { "0.1.0", "0.2.0", "0.3.0" }) Directory.CreateDirectory(SelfUpdate.StagingFor(v, g.Root));
        SelfUpdate.CleanStaging("0.2.0", g.Root);
        Assert.False(Directory.Exists(SelfUpdate.StagingFor("0.1.0", g.Root)));
        Assert.False(Directory.Exists(SelfUpdate.StagingFor("0.2.0", g.Root)));
        Assert.True(Directory.Exists(SelfUpdate.StagingFor("0.3.0", g.Root)));
    }

    [Fact]
    public void SaysWhyItCantUpdate()
    {
        var r = new ReleaseInfo("1.0.0", "page", null, "sums");
        Assert.Contains("isn't the single-file exe", SelfUpdate.CannotUpdateReason(r, null));
        Assert.Contains("no exe", SelfUpdate.CannotUpdateReason(r, "C:/x/SurvivorsToolkit.exe"));
    }
}

public class UpdateHandOffTests
{
    [Fact]
    public void FinishArgumentsSayWhetherToRestart()
    {
        var quiet = SelfUpdate.FinishArgs(42, @"C:\Apps\SurvivorsToolkit.exe", "0.1.0", restart: false);
        Assert.Equal(SelfUpdate.FinishArg, quiet[0]);
        Assert.Equal((42, @"C:\Apps\SurvivorsToolkit.exe", "0.1.0", false), SelfUpdate.ParseFinishArgs(quiet[1..]));
        Assert.True(SelfUpdate.ParseFinishArgs(SelfUpdate.FinishArgs(42, "t", "0.1.0", restart: true)[1..])!.Value.Restart);
    }

    [Fact]
    public void AHandOffFromZeroOneZeroAlwaysRestarts() =>
        // 0.1.0 passes only the process id, target and version.
        Assert.Equal((7, "t", "0.1.0", true), SelfUpdate.ParseFinishArgs(new[] { "7", "t", "0.1.0" }));

    [Fact]
    public void MissingDetailsAreRefused()
    {
        Assert.Null(SelfUpdate.ParseFinishArgs(new[] { "7", "t" }));
        Assert.Null(SelfUpdate.ParseFinishArgs(new[] { "x", "t", "0.1.0" }));
    }
}

public class WhatsNewTests
{
    const string Log = "# Changelog\n\nIntro.\n\n## 1.4.0\n\n- **Big thing.** See [docs](https://x.test).\n\n## 1.3.10\n\n- `fix` one\n\n## 1.3.9\n\n- old\n\n## 1.0.0\n\nFirst release.\n";

    [Fact]
    public void SectionsSinceTheLastRunNewestFirst()
    {
        var s = WhatsNew.Between(Log, "1.3.9", "1.4.0");
        Assert.Equal(new[] { "1.4.0", "1.3.10" }, s.Select(x => x.Version));
        Assert.Equal("- `fix` one", s[1].Text);
        Assert.Empty(WhatsNew.Between(Log, "1.4.0", "1.4.0"));
        Assert.Equal(4, WhatsNew.Between(Log, "0", "1.4.0").Count);
    }

    [Theory]
    [InlineData("", "1.4.0", false)] // first run ever
    [InlineData("1.3.9", "1.4.0", true)]
    [InlineData("1.4.0", "1.4.0", false)]
    [InlineData("1.5.0", "1.4.0", false)] // went back a version
    public void ShownOnlyAfterAnUpdate(string last, string current, bool expected) => Assert.Equal(expected, WhatsNew.ShouldShow(last, current));

    [Fact]
    public void WrappedItemsAreJoinedIntoOneBulletEach()
    {
        var notes = "- **Updates wait.** Installed when you\n  close the app (`--finish-update`).\n  - Nested\n    item.\n\nA paragraph\nover two lines.\n\n### Fixes\n\n* Third";
        var b = WhatsNew.Blocks(new[] { ("1.4.0", notes) });
        Assert.Equal(new[] { NoteKind.Version, NoteKind.Bullet, NoteKind.Bullet, NoteKind.Text, NoteKind.Heading, NoteKind.Bullet }, b.Select(x => x.Kind));
        Assert.Equal("Updates wait. Installed when you close the app (--finish-update).", b[1].Text);
        Assert.Equal("Nested item.", b[2].Text);
        Assert.Equal("A paragraph over two lines.", b[3].Text);
    }

    [Fact]
    public void BoldCodeAndLinksBecomeRuns() =>
        Assert.Equal(new NoteSpan[] { new("Big thing.", Bold: true), new(" Run "), new("help", Code: true), new(", see docs.") },
            WhatsNew.Spans("**Big `thing`.** Run `help`, see [docs](https://x.test)."));

    [Fact]
    public void TheBuiltInChangelogCoversThisVersionAndReadsCleanly()
    {
        var all = WhatsNew.Between(WhatsNew.BuiltIn(), "0", "999.0");
        // The test runner is the entry assembly here, so the version comes from Core itself.
        var version = typeof(WhatsNew).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
        Assert.Contains(all, s => s.Version == version);
        var blocks = WhatsNew.Blocks(all);
        Assert.DoesNotContain(blocks, x => x.Text.Contains('\n') || x.Text.Contains("**") || x.Text.Contains('`') || x.Text.StartsWith(' '));
    }
}

public class WordMatchTests
{
    [Theory]
    [InlineData("Azrael's Bait Mod", "bait", true)]
    [InlineData("AzraelBait", "bait", false)]
    [InlineData("Bait", "Bait", true)]
    [InlineData("Bait_Plus", "bait", false)]
    [InlineData("SCore (Sphereii)", "score", true)]
    [InlineData("Super Bait 2", "bait 2", true)]
    [InlineData("baitbait bait", "bait", true)]
    public void MatchesWholeWordsOnly(string text, string phrase, bool expected) => Assert.Equal(expected, ModAnalysis.HasWord(text, phrase));
}
