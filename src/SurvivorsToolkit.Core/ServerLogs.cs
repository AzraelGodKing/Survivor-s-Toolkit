using System.IO.Compression;
using System.Text;

namespace SurvivorsToolkit.Core;

/// <summary>Which of the three daily files a line goes to.</summary>
public enum ServerLogKind { Errors, Warnings, Everything }

/// <summary>Where the server's log is read from: the log file of the dedicated server on this PC, or its telnet console.</summary>
public enum ServerLogSource { ThisPc, Telnet }

/// <summary>What the server log files keep, and for how long (saved in the settings).</summary>
public sealed class ServerLogPlan
{
    public bool Errors { get; set; } = true;
    public bool Warnings { get; set; } = true;
    public bool Everything { get; set; } = true;
    /// <summary>Days of files kept, today included; older ones are deleted when the day changes. 0 keeps them all.</summary>
    public int KeepDays { get; set; } = 14;
    /// <summary>Where the files go when not <see cref="ServerLogWriter.DefaultFolder"/>.</summary>
    public string? Folder { get; set; }
    public ServerLogSource Source { get; set; } = ServerLogSource.ThisPc;
    /// <summary>Start saving the server's log as soon as the app opens.</summary>
    public bool StartWithApp { get; set; }

    public bool Any => Errors || Warnings || Everything;

    public string FolderOrDefault => string.IsNullOrWhiteSpace(Folder) ? ServerLogWriter.DefaultFolder : Folder.Trim();
}

/// <summary>Text that arrives in pieces, cut into whole lines (a piece can end in the middle of one).</summary>
public sealed class LineSplitter
{
    readonly StringBuilder _partial = new();

    /// <summary>The lines <paramref name="text"/> finishes; the unfinished end is kept for the next piece.</summary>
    public List<string> Push(string text)
    {
        var lines = new List<string>();
        foreach (var c in text)
        {
            if (c == '\n')
            {
                lines.Add(_partial.ToString().TrimEnd('\r'));
                _partial.Clear();
            }
            else _partial.Append(c);
        }
        return lines;
    }

    /// <summary>The unfinished line, if any, when no more text is coming.</summary>
    public string? Flush()
    {
        var rest = _partial.ToString().TrimEnd('\r');
        _partial.Clear();
        return rest.Length > 0 ? rest : null;
    }
}

/// <summary>A server console that prints the server's log as it happens (7 Days to Die's telnet console does).</summary>
public interface IServerLogStream : IDisposable
{
    /// <summary>Hands every line the console prints to <paramref name="line"/> until the link drops (<see cref="ServerConsoleException"/>) or <paramref name="ct"/> is cancelled.</summary>
    Task ListenAsync(Action<string> line, CancellationToken ct);
}

/// <summary>
/// Follows a log file the server is still writing: each read returns the lines added since the last one. When the
/// server starts again it writes a new file; the rest of the old one is read, then the new one from its start.
/// </summary>
public sealed class LogTail
{
    readonly Func<string?> _newest;
    readonly LineSplitter _split = new();
    Decoder _decoder = Encoding.UTF8.GetDecoder();
    long _at;
    bool _started;

    /// <param name="newest">The log the server writes now, or null when there is none yet.</param>
    public LogTail(Func<string?> newest) => _newest = newest;

    /// <summary>The file being followed, or null before there is one.</summary>
    public string? Path { get; private set; }

    /// <summary>
    /// The lines written since the last read. The first file found is followed from its end, so what the server wrote
    /// before saving started isn't saved again; a file that appears later is read from its start.
    /// </summary>
    public List<string> ReadNew()
    {
        var lines = new List<string>();
        var newest = _newest();
        if (newest != null && !string.Equals(newest, Path, StringComparison.OrdinalIgnoreCase))
        {
            if (Path != null)
            {
                Read(lines);
                if (_split.Flush() is { } last) lines.Add(last);
            }
            Path = newest;
            _decoder = Encoding.UTF8.GetDecoder();
            _at = _started ? 0 : SafeLength(newest);
        }
        _started = true;
        if (Path != null) Read(lines);
        return lines;
    }

    void Read(List<string> lines)
    {
        if (Path == null || !File.Exists(Path)) return;
        using var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // A shorter file than before was started over: read it again from the top.
        if (fs.Length < _at) { _at = 0; _decoder = Encoding.UTF8.GetDecoder(); }
        fs.Seek(_at, SeekOrigin.Begin);
        var buffer = new byte[64 * 1024];
        var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            var n = _decoder.GetChars(buffer, 0, read, chars, 0);
            lines.AddRange(_split.Push(new string(chars, 0, n)));
        }
        _at = fs.Position;
    }

    static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return 0; }
    }
}

/// <summary>
/// Writes the server's log into three files a day in one folder: errors-yyyy-MM-dd.log (ERR and EXC entries with their
/// stack traces), warnings-yyyy-MM-dd.log (WRN) and everything-yyyy-MM-dd.log (every line), each only when the plan asks
/// for it. A new set starts at midnight and sets older than <see cref="ServerLogPlan.KeepDays"/> are deleted then.
/// The files can be opened while they are written. Safe to call from any thread.
/// </summary>
public sealed class ServerLogWriter : IDisposable
{
    public static string DefaultFolder => System.IO.Path.Combine(AppSettings.Folder, "server-logs");

    public static string FileName(ServerLogKind kind, DateTime day) => $"{kind.ToString().ToLowerInvariant()}-{day:yyyy-MM-dd}.log";

    readonly object _lock = new();
    readonly ServerLogPlan _plan;
    readonly Func<DateTime> _now;
    readonly Dictionary<ServerLogKind, StreamWriter> _open = new();
    DateTime _day;
    LogLevel _level = LogLevel.Info;
    bool _closed;

    public ServerLogWriter(string folder, ServerLogPlan plan, Func<DateTime>? now = null)
    {
        Folder = folder;
        _plan = plan;
        _now = now ?? (() => DateTime.Now);
    }

    public string Folder { get; }

    /// <summary>Today's counts: errors and warnings (entries, not their stack trace lines) and every line.</summary>
    public int Errors { get; private set; }
    public int Warnings { get; private set; }
    public int Lines { get; private set; }

    /// <summary>The day the counts are for.</summary>
    public DateTime Day { get { lock (_lock) return _day; } }

    public void Add(string line)
    {
        lock (_lock)
        {
            if (_closed) return;
            var today = _now().Date;
            if (today != _day) Rotate(today);
            // A line that doesn't start an entry is its stack trace, or text that runs on, so it keeps the entry's level.
            if (LogAnalysis.LevelOf(line) is { } level)
            {
                _level = level;
                if (level == LogLevel.Error) Errors++;
                else if (level == LogLevel.Warning) Warnings++;
            }
            Lines++;
            if (_plan.Everything) Write(ServerLogKind.Everything, line);
            if (_plan.Errors && _level == LogLevel.Error) Write(ServerLogKind.Errors, line);
            if (_plan.Warnings && _level == LogLevel.Warning) Write(ServerLogKind.Warnings, line);
        }
    }

    void Write(ServerLogKind kind, string line)
    {
        if (!_open.TryGetValue(kind, out var w))
        {
            Directory.CreateDirectory(Folder);
            var fs = new FileStream(System.IO.Path.Combine(Folder, FileName(kind, _day)), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _open[kind] = w = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }
        w.WriteLine(line);
    }

    void Rotate(DateTime today)
    {
        Close();
        _day = today;
        Errors = Warnings = Lines = 0;
        Prune(Folder, _plan.KeepDays, today);
    }

    /// <summary>Deletes the files of days older than the newest <paramref name="keepDays"/> (today included); 0 keeps all.</summary>
    public static void Prune(string folder, int keepDays, DateTime today)
    {
        if (keepDays <= 0) return;
        var oldest = today.Date.AddDays(1 - keepDays);
        foreach (var (day, files) in Files(folder))
        {
            if (day >= oldest) continue;
            foreach (var f in files)
            {
                try { File.Delete(f); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>The days that have files in <paramref name="folder"/>, newest first, with their files.</summary>
    public static List<(DateTime Day, List<string> Files)> Files(string folder)
    {
        if (!Directory.Exists(folder)) return new();
        var days = new Dictionary<DateTime, List<string>>();
        foreach (var f in Directory.GetFiles(folder, "*.log"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(f);
            var dash = name.IndexOf('-');
            if (dash < 0 || !Enum.TryParse<ServerLogKind>(name[..dash], true, out _)
                || !DateTime.TryParseExact(name[(dash + 1)..], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)) continue;
            if (!days.TryGetValue(day, out var list)) days[day] = list = new();
            list.Add(f);
        }
        return days.OrderByDescending(d => d.Key).Select(d => (d.Key, d.Value.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList())).ToList();
    }

    /// <summary>
    /// Zips the files of <paramref name="days"/> into the folder's "shared" folder, to send to someone, and returns the
    /// zip. Throws <see cref="FileNotFoundException"/> when those days have no files.
    /// </summary>
    public static string Zip(string folder, IEnumerable<DateTime> days)
    {
        var wanted = days.Select(d => d.Date).ToHashSet();
        var picked = Files(folder).Where(d => wanted.Contains(d.Day)).ToList();
        if (picked.Count == 0) throw new FileNotFoundException("There are no server log files for that day yet.");
        var first = picked.Min(d => d.Day);
        var last = picked.Max(d => d.Day);
        var shared = System.IO.Path.Combine(folder, "shared");
        Directory.CreateDirectory(shared);
        var zip = System.IO.Path.Combine(shared, first == last ? $"server-logs-{first:yyyy-MM-dd}.zip" : $"server-logs-{first:yyyy-MM-dd}-to-{last:yyyy-MM-dd}.zip");
        if (File.Exists(zip)) File.Delete(zip);
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        foreach (var f in picked.SelectMany(d => d.Files))
        {
            // The file is still being written today, so it is copied through a stream that lets the writer carry on.
            using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var dst = archive.CreateEntry(System.IO.Path.GetFileName(f), CompressionLevel.Optimal).Open();
            src.CopyTo(dst);
        }
        return zip;
    }

    void Close()
    {
        foreach (var w in _open.Values)
        {
            try { w.Dispose(); } catch (IOException) { }
        }
        _open.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _closed = true;
            Close();
        }
    }
}

/// <summary>Following the server's log into a <see cref="ServerLogWriter"/>, from either source, until cancelled.</summary>
public static class ServerLogs
{
    /// <summary>The newest output_log*.txt in the server's *_Data folders (where -logfile puts it), or null.</summary>
    public static string? NewestLog(ServerInstall server)
    {
        if (!Directory.Exists(server.Folder)) return null;
        try
        {
            return Directory.GetDirectories(server.Folder, "*_Data")
                .SelectMany(d => Directory.GetFiles(d, "output_log*.txt"))
                .Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Reads the server's log file every <paramref name="every"/> (two seconds) and saves the new lines.</summary>
    public static async Task FollowFileAsync(ServerLogWriter writer, Func<string?> newest, Action<string> status, CancellationToken ct, TimeSpan? every = null)
    {
        var tail = new LogTail(newest);
        string? said = null;
        while (!ct.IsCancellationRequested)
        {
            string now;
            try
            {
                foreach (var line in tail.ReadNew()) writer.Add(line);
                now = tail.Path == null ? "Waiting for the dedicated server on this PC to write a log." : "Saving the log of the server on this PC: " + tail.Path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { now = "Couldn't read the server's log: " + e.Message; }
            if (now != said) status(said = now);
            try { await Task.Delay(every ?? TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// Listens to the server's console, which prints its log as it happens, and saves every line. When the link drops
    /// (the server restarted, or the internet hiccuped) it connects again after <paramref name="retry"/> (30 seconds).
    /// </summary>
    public static async Task FollowConsoleAsync(ServerLogWriter writer, Func<CancellationToken, Task<IServerLogStream>> connect, string describe, Action<string> status, CancellationToken ct, TimeSpan? retry = null)
    {
        var wait = retry ?? TimeSpan.FromSeconds(30);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                status($"Connecting to {describe}...");
                using var console = await connect(ct);
                status($"Saving the log of {describe} from its console.");
                await console.ListenAsync(writer.Add, ct);
            }
            catch (ServerConsoleException e) { status($"{e.Message} Trying again in {wait.TotalSeconds:0} seconds."); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status($"Couldn't write the log files: {e.Message} Trying again in {wait.TotalSeconds:0} seconds."); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }
}
