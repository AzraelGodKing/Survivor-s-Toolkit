namespace SurvivorsToolkit.Tests;

public class UpdateCheckTests
{
    static string Release(string tag, bool draft = false, bool pre = false, string body = "notes") => $$"""
        { "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{pre.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/x/{{tag}}", "body": "{{body}}",
          "assets": [
            { "name": "SurvivorsToolkit-{{tag[1..]}}-win-x64.exe", "browser_download_url": "https://dl/{{tag}}/app.exe" },
            { "name": "SurvivorsToolkit-{{tag[1..]}}-win-x64-framework-dependent.zip", "browser_download_url": "https://dl/{{tag}}/app.zip" },
            { "name": "SurvivorsToolkit-{{tag[1..]}}-Setup.exe", "browser_download_url": "https://dl/{{tag}}/Setup.exe" },
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
        Assert.Equal("https://dl/v0.10.0/app.zip", r.ZipUrl);
        Assert.Equal("https://dl/v0.10.0/Setup.exe", r.SetupUrl);
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
        Assert.Contains("isn't run from its exe", SelfUpdate.CannotUpdateReason(r, null));
        Assert.Contains("no exe", SelfUpdate.CannotUpdateReason(r, "C:/x/SurvivorsToolkit.exe"));
    }

    [Fact]
    public void AFolderBuildUpdatesFromTheZip()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var exe = Path.Combine(g.Root, SelfUpdate.ExeName);
        File.WriteAllText(exe, "");
        Assert.Equal(InstallKind.SingleFile, SelfUpdate.KindOf(g.Root));
        File.WriteAllText(Path.Combine(g.Root, "SurvivorsToolkit.dll"), "");
        Assert.Equal(InstallKind.Folder, SelfUpdate.KindOf(g.Root));

        var r = new ReleaseInfo("1.0.0", "page", "exe", "sums");
        Assert.Contains("framework-dependent zip", SelfUpdate.CannotUpdateReason(r, exe));
        Assert.Null(SelfUpdate.CannotUpdateReason(r with { ZipUrl = "zip" }, exe));
        Assert.Equal("zip", SelfUpdate.AssetFor(r with { ZipUrl = "zip" }, InstallKind.Folder));
    }

    [Fact]
    public void ApplyFolderKeepsTheOldFilesAndPutBackRestoresThem()
    {
        using var g = new FakeGame();
        var app = Path.Combine(g.Root, "app");
        var src = Path.Combine(g.Root, "new");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(Path.Combine(src, "runtimes"));
        File.WriteAllText(Path.Combine(app, "SurvivorsToolkit.dll"), "old");
        File.WriteAllText(Path.Combine(src, "SurvivorsToolkit.dll"), "new");
        File.WriteAllText(Path.Combine(src, "runtimes", "extra.dll"), "added");

        SelfUpdate.ApplyFolder(src, app);
        Assert.Equal("new", File.ReadAllText(Path.Combine(app, "SurvivorsToolkit.dll")));
        Assert.Equal("added", File.ReadAllText(Path.Combine(app, "runtimes", "extra.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "SurvivorsToolkit.dll" + SelfUpdate.OldSuffix)));

        Assert.True(SelfUpdate.PutBackFolder(src, app));
        Assert.Equal("old", File.ReadAllText(Path.Combine(app, "SurvivorsToolkit.dll")));
        Assert.False(File.Exists(Path.Combine(app, "runtimes", "extra.dll")));
    }

    [Fact]
    public async Task PrepareUnpacksAndChecksTheZip()
    {
        using var g = new FakeGame();
        Directory.CreateDirectory(g.Root);
        var zip = Path.Combine(g.Root, "build.zip");
        using (var a = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            foreach (var name in new[] { SelfUpdate.ExeName, "SurvivorsToolkit.dll" })
                using (var w = new StreamWriter(a.CreateEntry(name).Open())) w.Write(name);
        var bytes = File.ReadAllBytes(zip);
        const string zipName = "SurvivorsToolkit-1.0.0-win-x64-framework-dependent.zip";
        var http = new HttpClient(new Serve(new()
        {
            ["https://dl/SHA256SUMS.txt"] = System.Text.Encoding.ASCII.GetBytes($"{SelfUpdate.Sha256Of(zip)}  {zipName}\n"),
            ["https://dl/" + zipName] = bytes,
        }));
        var r = new ReleaseInfo("1.0.0", "page", null, "https://dl/SHA256SUMS.txt", ZipUrl: "https://dl/" + zipName);

        var u = await SelfUpdate.PrepareAsync(r, InstallKind.Folder, null, CancellationToken.None, http, g.Root);
        Assert.Equal(InstallKind.Folder, u.Kind);
        Assert.Equal(SelfUpdate.ExeName, File.ReadAllText(u.Exe));
        Assert.True(File.Exists(Path.Combine(u.FilesDir, "SurvivorsToolkit.dll")));
    }

    sealed class Serve(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(files.TryGetValue(req.RequestUri!.ToString(), out var b)
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
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

