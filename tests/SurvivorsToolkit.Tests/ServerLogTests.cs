using System.IO.Compression;

public sealed class ServerLogTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "st-serverlog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    const string Info = "2026-10-27T21:00:00 10.000 INF Server started";
    const string Warn = "2026-10-27T21:00:01 11.000 WRN Shader missing";
    const string Err = "2026-10-27T21:00:02 12.000 ERR Something broke";
    const string Exc = "2026-10-27T21:00:03 13.000 EXC NullReferenceException";
    const string Trace = "  at SomeMod.Patch.Postfix ()";

    string Read(ServerLogKind kind, DateTime day)
    {
        var path = Path.Combine(_dir, ServerLogWriter.FileName(kind, day));
        if (!File.Exists(path)) return "";
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return new StreamReader(fs).ReadToEnd().Replace("\r\n", "\n");
    }

    [Fact]
    public void SplitsErrorsWarningsAndEverythingIntoTheirOwnFiles()
    {
        var day = new DateTime(2026, 10, 27, 21, 0, 0);
        using (var w = new ServerLogWriter(_dir, new ServerLogPlan(), () => day))
        {
            foreach (var l in new[] { Info, Warn, Err, Trace, Exc, Trace, Info }) w.Add(l);
            Assert.Equal((2, 1, 7), (w.Errors, w.Warnings, w.Lines));
        }
        Assert.Equal($"{Err}\n{Trace}\n{Exc}\n{Trace}\n", Read(ServerLogKind.Errors, day));
        Assert.Equal($"{Warn}\n", Read(ServerLogKind.Warnings, day));
        Assert.Equal(7, Read(ServerLogKind.Everything, day).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void WritesOnlyTheFilesThatAreTicked()
    {
        var day = new DateTime(2026, 10, 27);
        using (var w = new ServerLogWriter(_dir, new ServerLogPlan { Warnings = false, Everything = false }, () => day))
            foreach (var l in new[] { Info, Warn, Err }) w.Add(l);
        Assert.Equal($"{Err}\n", Read(ServerLogKind.Errors, day));
        Assert.False(File.Exists(Path.Combine(_dir, ServerLogWriter.FileName(ServerLogKind.Warnings, day))));
        Assert.False(File.Exists(Path.Combine(_dir, ServerLogWriter.FileName(ServerLogKind.Everything, day))));
    }

    [Fact]
    public void StartsNewFilesAtMidnightAndDeletesOldDays()
    {
        Directory.CreateDirectory(_dir);
        var old = new DateTime(2026, 10, 20);
        File.WriteAllText(Path.Combine(_dir, ServerLogWriter.FileName(ServerLogKind.Errors, old)), "old");
        File.WriteAllText(Path.Combine(_dir, "notes.log"), "not ours");
        var now = new DateTime(2026, 10, 27, 23, 59, 0);
        using (var w = new ServerLogWriter(_dir, new ServerLogPlan { KeepDays = 2 }, () => now))
        {
            w.Add(Err);
            now = now.AddMinutes(2);
            w.Add(Warn);
            Assert.Equal((0, 1), (w.Errors, w.Warnings));
            Assert.Equal(now.Date, w.Day);
        }
        Assert.Equal($"{Err}\n", Read(ServerLogKind.Errors, new DateTime(2026, 10, 27)));
        Assert.Equal($"{Warn}\n", Read(ServerLogKind.Warnings, new DateTime(2026, 10, 28)));
        Assert.Equal("", Read(ServerLogKind.Warnings, new DateTime(2026, 10, 27)));
        Assert.False(File.Exists(Path.Combine(_dir, ServerLogWriter.FileName(ServerLogKind.Errors, old))));
        Assert.True(File.Exists(Path.Combine(_dir, "notes.log")));
        Assert.Equal(new[] { new DateTime(2026, 10, 28), new DateTime(2026, 10, 27) }, ServerLogWriter.Files(_dir).Select(d => d.Day));
    }

    [Fact]
    public void ZipsADayToShare()
    {
        var day = new DateTime(2026, 10, 27);
        using var w = new ServerLogWriter(_dir, new ServerLogPlan(), () => day);
        w.Add(Err);
        // Still open for writing, as it is while the server runs.
        var zip = ServerLogWriter.Zip(_dir, new[] { day, day.AddDays(-1) });
        Assert.Equal("server-logs-2026-10-27.zip", Path.GetFileName(zip));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "errors-2026-10-27.log", "everything-2026-10-27.log" }, archive.Entries.Select(e => e.Name).OrderBy(n => n));
        Assert.Throws<FileNotFoundException>(() => ServerLogWriter.Zip(_dir, new[] { day.AddDays(-5) }));
    }

    [Fact]
    public void SplitsTextThatArrivesInPieces()
    {
        var s = new LineSplitter();
        Assert.Empty(s.Push("2026 INF hel"));
        Assert.Equal(new[] { "2026 INF hello", "second" }, s.Push("lo\r\nsecond\nthi"));
        Assert.Equal("thi", s.Flush());
        Assert.Null(s.Flush());
    }

    [Fact]
    public void FollowsTheLogFileFromItsEndThenANewFileFromItsStart()
    {
        Directory.CreateDirectory(_dir);
        var first = Path.Combine(_dir, "output_log_dedi__1.txt");
        File.WriteAllText(first, "before we started\n");
        var newest = first;
        var tail = new LogTail(() => newest);
        Assert.Empty(tail.ReadNew());

        File.AppendAllText(first, "one\ntw");
        Assert.Equal(new[] { "one" }, tail.ReadNew());
        File.AppendAllText(first, "o\n");
        Assert.Equal(new[] { "two" }, tail.ReadNew());

        // The server started again: the rest of the old file, then all of the new one.
        File.AppendAllText(first, "last words\n");
        var second = Path.Combine(_dir, "output_log_dedi__2.txt");
        File.WriteAllText(second, "fresh start\n");
        newest = second;
        Assert.Equal(new[] { "last words", "fresh start" }, tail.ReadNew());
        Assert.Equal(second, tail.Path);
    }

    [Fact]
    public void FindsTheServersNewestLog()
    {
        var data = Path.Combine(_dir, "7DaysToDieServer_Data");
        Directory.CreateDirectory(data);
        var a = Path.Combine(data, "output_log_dedi__a.txt");
        var b = Path.Combine(data, "output_log_dedi__b.txt");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(-1));
        Assert.Equal(b, ServerLogs.NewestLog(DedicatedServer.At(_dir)));
    }

    sealed class FakeStream : IServerLogStream
    {
        readonly string[] _lines;
        public FakeStream(params string[] lines) => _lines = lines;
        public Task ListenAsync(Action<string> line, CancellationToken ct)
        {
            foreach (var l in _lines) line(l);
            throw new ServerConsoleException("The server closed the console link.");
        }
        public void Dispose() { }
    }

    [Fact]
    public async Task SavesTheConsolesLogAndConnectsAgainWhenTheLinkDrops()
    {
        var day = new DateTime(2026, 10, 27);
        using var w = new ServerLogWriter(_dir, new ServerLogPlan(), () => day);
        using var stop = new CancellationTokenSource();
        var said = new List<string>();
        var connects = 0;
        await ServerLogs.FollowConsoleAsync(w, _ =>
        {
            if (++connects == 2) stop.Cancel();
            return Task.FromResult<IServerLogStream>(connects == 1 ? new FakeStream(Err, Trace) : new FakeStream(Warn));
        }, "test:8081", said.Add, stop.Token, TimeSpan.Zero);
        Assert.Equal(2, connects);
        Assert.Equal($"{Err}\n{Trace}\n", Read(ServerLogKind.Errors, day));
        Assert.Equal($"{Warn}\n", Read(ServerLogKind.Warnings, day));
        Assert.Contains(said, s => s.Contains("Trying again"));
    }
}
