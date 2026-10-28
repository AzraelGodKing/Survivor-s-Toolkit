using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SurvivorsToolkit.Core;

/// <summary>How this copy of the program was installed, which decides what an update downloads.</summary>
public enum InstallKind
{
    /// <summary>The standalone self-contained exe: updated by swapping that one file.</summary>
    SingleFile,
    /// <summary>The framework-dependent folder build: updated from the release zip.</summary>
    Folder,
}

/// <summary>A downloaded, checked update waiting in <see cref="StagingDir"/> to be installed.</summary>
public sealed record PreparedUpdate(string Version, string StagingDir, InstallKind Kind = InstallKind.SingleFile)
{
    /// <summary>The unpacked folder build in the staging folder (only for <see cref="InstallKind.Folder"/>).</summary>
    public string FilesDir => Path.Combine(StagingDir, "files");

    /// <summary>The downloaded program in its staging folder.</summary>
    public string Exe => Path.Combine(Kind == InstallKind.SingleFile ? StagingDir : FilesDir, SelfUpdate.ExeName);
}

/// <summary>
/// Updates the program in place from a GitHub release, the way ModWright does: the single-file exe from the release's
/// exe, the framework-dependent folder build from its zip. (A copy installed with the Setup.exe updates through Velopack
/// instead; see AppUpdates in the app.) The download is accepted only when its SHA-256 matches the release's
/// SHA256SUMS.txt and it carries the expected version. Installing renames each file being replaced to
/// "&lt;name&gt;.update-old", copies the new one in, and puts the old ones back if any step fails; the next start deletes
/// the leftovers.
/// <para>The running program never swaps its own file: .NET reads parts of a single-file program from its exe for as
/// long as it runs. Instead the downloaded program is started from the staging folder (<see cref="HandOff"/>); it waits
/// for this one to exit, installs itself (<see cref="Finish"/>) and, when asked, starts the installed copy. So an update
/// can wait until the window is closed, the way ModWright installs one "when I close the app".</para>
/// </summary>
public static class SelfUpdate
{
    public const string OldSuffix = ".update-old";
    public const string ExeName = "SurvivorsToolkit.exe";
    const string DllName = "SurvivorsToolkit.dll";
    const long MaxDownloadBytes = 400L * 1024 * 1024;

    /// <summary>
    /// Passed to the downloaded program: "--finish-update &lt;process id&gt; &lt;target exe&gt; &lt;version replaced&gt; restart|quiet".
    /// "quiet" installs without starting the new version (the update was left for when the app closes).
    /// </summary>
    public const string FinishArg = "--finish-update";
    /// <summary>Passed to the new program after an update, followed by the version it replaced.</summary>
    public const string UpdatedFromArg = "--updated-from";

    /// <summary>The running exe, or null when the program isn't run from an exe (dotnet run can't update itself).</summary>
    public static string? RunningExe()
    {
        var path = Environment.ProcessPath;
        return path != null && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>A folder build has the app's own DLL next to the exe; the single-file exe has it bundled inside.</summary>
    public static InstallKind KindOf(string appDir) =>
        File.Exists(Path.Combine(appDir, DllName)) ? InstallKind.Folder : InstallKind.SingleFile;

    /// <summary>Where an update for <paramref name="kind"/> comes from, or null when the release has no file for it.</summary>
    public static string? AssetFor(ReleaseInfo r, InstallKind kind) => kind == InstallKind.SingleFile ? r.ExeUrl : r.ZipUrl;

    /// <summary>Why this copy can't update itself from <paramref name="r"/> (then the release page is offered), or null when it can.</summary>
    public static string? CannotUpdateReason(ReleaseInfo r, string? exe)
    {
        if (exe == null) return "This copy isn't run from its exe, so it can't replace itself.";
        if (AssetFor(r, KindOf(Path.GetDirectoryName(exe)!)) == null)
            return KindOf(Path.GetDirectoryName(exe)!) == InstallKind.SingleFile
                ? "The release has no exe to download."
                : "The release has no framework-dependent zip to update this folder from.";
        if (r.ChecksumsUrl == null) return "The release has no SHA256SUMS.txt to check the download against.";
        if (!CanWrite(Path.GetDirectoryName(exe)!)) return $"This program's folder ({Path.GetDirectoryName(exe)}) can't be written to without administrator rights.";
        return null;
    }

    public static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The file name at the end of a download URL.</summary>
    public static string FileNameOf(string url) => Uri.UnescapeDataString(new Uri(url).Segments[^1]);

    /// <summary>Reads a sha256sum listing ("&lt;hex&gt;  &lt;name&gt;" or "&lt;hex&gt; *&lt;name&gt;"), keyed by file name.</summary>
    public static Dictionary<string, string> ParseChecksums(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var space = line.IndexOf(' ');
            if (space != 64) continue;
            var hash = line[..64];
            if (!hash.All(Uri.IsHexDigit)) continue;
            var name = line[space..].TrimStart(' ', '*').Trim();
            if (name.Length > 0) d[name] = hash.ToLowerInvariant();
        }
        return d;
    }

    public static string Sha256Of(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    public static string StagingFor(string version, string? tempRoot = null) =>
        Path.Combine(tempRoot ?? Path.GetTempPath(), "SurvivorsToolkit-update", version);

    /// <summary>
    /// Downloads the release's file for <paramref name="kind"/> (the exe, or the zip, which is unpacked) into a staging
    /// folder and checks it against SHA256SUMS.txt. <paramref name="progress"/> gets 0-100. Throws with a readable
    /// message when anything is wrong; nothing is changed until <see cref="HandOff"/>.
    /// </summary>
    public static async Task<PreparedUpdate> PrepareAsync(ReleaseInfo r, InstallKind kind, IProgress<int>? progress, CancellationToken ct, HttpClient? http = null, string? tempRoot = null)
    {
        var url = AssetFor(r, kind) ?? throw new InvalidOperationException("The release has no download for this kind of install.");
        if (r.ChecksumsUrl == null) throw new InvalidOperationException("The release has no SHA256SUMS.txt to check the download against.");
        http ??= UpdateCheck.CreateClient(TimeSpan.FromMinutes(10));

        var sums = ParseChecksums(await http.GetStringAsync(r.ChecksumsUrl, ct));
        var name = FileNameOf(url);
        if (!sums.TryGetValue(name, out var expected)) throw new InvalidDataException($"SHA256SUMS.txt doesn't list {name}.");

        var staging = StagingFor(r.Version, tempRoot);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        var download = Path.Combine(staging, name + ".part");
        await DownloadAsync(http, url, download, progress, ct);

        var actual = Sha256Of(download);
        if (actual != expected)
            throw new InvalidDataException($"The download doesn't match its checksum (expected {expected[..12]}..., got {actual[..12]}...). Nothing was changed.");
        var u = new PreparedUpdate(r.Version, staging, kind);
        if (kind == InstallKind.SingleFile)
            File.Move(download, u.Exe);
        else
        {
            ZipFile.ExtractToDirectory(download, u.FilesDir); // refuses entries that would land outside the folder
            File.Delete(download);
            if (!File.Exists(u.Exe) || !File.Exists(Path.Combine(u.FilesDir, DllName)))
                throw new InvalidDataException($"The downloaded zip doesn't hold {ExeName} and {DllName}.");
        }
        var got = VersionOf(kind == InstallKind.SingleFile ? u.Exe : Path.Combine(u.FilesDir, DllName));
        if (got != null && got != r.Version)
            throw new InvalidDataException($"The download says it is version {got}, not {r.Version}. Nothing was changed.");
        progress?.Report(100);
        return u;
    }

    static string? VersionOf(string file)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(file).ProductVersion;
            return string.IsNullOrWhiteSpace(v) ? null : v.Split('+')[0].Trim();
        }
        catch (Exception e) when (e is IOException or FileNotFoundException) { return null; }
    }

    static async Task DownloadAsync(HttpClient http, string url, string dest, IProgress<int>? progress, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;
        if (total > MaxDownloadBytes) throw new InvalidDataException($"The download is {total} bytes, far more than expected.");
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long done = 0;
        int read, last = -1;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            done += read;
            if (done > MaxDownloadBytes) throw new InvalidDataException("The download is far larger than expected.");
            if (total > 0)
            {
                var pct = (int)(done * 99 / total.Value);
                if (pct != last) { progress?.Report(pct); last = pct; }
            }
        }
        if (total is long n && n != done) throw new InvalidDataException($"The download is incomplete ({done} of {n} bytes).");
    }

    /// <summary>
    /// Puts <paramref name="newExe"/> in place of <paramref name="target"/>, keeping the old one as "&lt;target&gt;.update-old"
    /// until the next start. If the copy fails the old exe is put back and the error is thrown.
    /// </summary>
    public static void Apply(string newExe, string target)
    {
        var old = target + OldSuffix;
        var hadOld = File.Exists(target);
        if (hadOld) File.Move(target, old, overwrite: true);
        try { File.Copy(newExe, target); }
        catch
        {
            try
            {
                if (File.Exists(target)) File.Delete(target);
                if (hadOld) File.Move(old, target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the .update-old copy is still there to restore by hand */ }
            throw;
        }
    }

    /// <summary>
    /// Copies every file of the folder build in <paramref name="src"/> over <paramref name="appDir"/>, keeping each one it
    /// replaces as "&lt;name&gt;.update-old" until the next start. If any copy fails, the files already swapped are put back
    /// and the error is thrown.
    /// </summary>
    public static void ApplyFolder(string src, string appDir)
    {
        var done = new List<(string Target, bool HadOld)>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(appDir, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var hadOld = File.Exists(target);
                if (hadOld) File.Move(target, target + OldSuffix, overwrite: true);
                done.Add((target, hadOld));
                File.Copy(file, target);
            }
        }
        catch
        {
            for (var i = done.Count - 1; i >= 0; i--)
            {
                var (target, hadOld) = done[i];
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (hadOld) File.Move(target + OldSuffix, target);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the .update-old copy is still there to restore by hand */ }
            }
            throw;
        }
    }

    /// <summary>Undoes <see cref="ApplyFolder"/>: each file it replaced gets its ".update-old" copy back. False when that failed.</summary>
    public static bool PutBackFolder(string src, string appDir)
    {
        var ok = true;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(appDir, Path.GetRelativePath(src, file));
            try
            {
                if (File.Exists(target + OldSuffix)) File.Move(target + OldSuffix, target, overwrite: true);
                else if (File.Exists(target)) File.Delete(target);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ok = false; }
        }
        return ok;
    }

    /// <summary>Deletes the "*.update-old" a previous update left next to <paramref name="exe"/>, retrying while the old process closes.</summary>
    public static int CleanUp(string exe, int attempts = 10)
    {
        var dir = Path.GetDirectoryName(exe)!;
        // A standalone exe often sits in Downloads or on the desktop: only look next to it, not in every sub-folder.
        var depth = KindOf(dir) == InstallKind.Folder ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var removed = 0;
        for (var i = 0; i < attempts; i++)
        {
            var left = Directory.EnumerateFiles(dir, "*" + OldSuffix, depth).ToList();
            if (left.Count == 0) break;
            foreach (var f in left)
                try { File.Delete(f); removed++; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (i + 1 < attempts) Thread.Sleep(500);
        }
        return removed;
    }

    /// <summary>Deletes downloaded updates that are installed or out of date. A download newer than <paramref name="current"/> is kept.</summary>
    public static void CleanStaging(string current, string? tempRoot = null)
    {
        var root = Path.GetDirectoryName(StagingFor("x", tempRoot))!;
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root))
            if (!UpdateCheck.IsNewer(Path.GetFileName(dir), current))
                try { Directory.Delete(dir, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Starts the downloaded program to install itself over <paramref name="target"/> as soon as this process exits,
    /// then (with <paramref name="restart"/>) start the installed copy. Nothing is changed until this process is gone.
    /// </summary>
    public static void HandOff(PreparedUpdate u, string target, string fromVersion, bool restart = true)
    {
        if (!File.Exists(u.Exe)) throw new FileNotFoundException("The downloaded update is no longer in " + u.StagingDir + ".", u.Exe);
        var psi = new ProcessStartInfo(u.Exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(u.Exe)! };
        foreach (var a in FinishArgs(Environment.ProcessId, target, fromVersion, restart)) psi.ArgumentList.Add(a);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>The arguments <see cref="HandOff"/> starts the downloaded program with.</summary>
    public static string[] FinishArgs(int pid, string target, string fromVersion, bool restart) =>
        new[] { FinishArg, pid.ToString(), target, fromVersion, restart ? "restart" : "quiet" };

    /// <summary>
    /// Reads the arguments after <see cref="FinishArg"/>; null when they are missing. A hand-off from a version before
    /// "restart|quiet" existed has no fourth argument and always restarted.
    /// </summary>
    public static (int Pid, string Target, string FromVersion, bool Restart)? ParseFinishArgs(IReadOnlyList<string> args)
    {
        if (args.Count < 3 || !int.TryParse(args[0], out var pid)) return null;
        return (pid, args[1], args[2], args.Count < 4 || args[3] != "quiet");
    }

    /// <summary>
    /// The installer side of <see cref="HandOff"/>, run by the downloaded program: waits for the old program to exit,
    /// copies itself over it and starts it. <paramref name="args"/> are the ones after <see cref="FinishArg"/>. Returns
    /// the exit code; <paramref name="fail"/> gets the reason when nothing could be installed.
    /// </summary>
    public static int Finish(IReadOnlyList<string> args, Action<string> fail)
    {
        if (ParseFinishArgs(args) is not { } parsed) { fail("The update was started without its details."); return 2; }
        var (pid, target, fromVersion, restart) = parsed;
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("No program path.");
        var selfDir = Path.GetDirectoryName(self)!;
        var appDir = Path.GetDirectoryName(target)!;
        var kind = KindOf(selfDir);
        var version = UpdateCheck.CurrentVersion;
        Log($"Installing {version} ({kind}) over {target} (replacing {fromVersion}, process {pid}).");
        try
        {
            WaitForExit(pid, TimeSpan.FromMinutes(2));
            // Renaming a running exe works on Windows, but that copy then crashes the next time .NET reads from it:
            // wait until nothing has it open.
            WaitUntilFree(target, TimeSpan.FromMinutes(2));
            if (kind == InstallKind.Folder) WaitUntilFree(Path.Combine(appDir, DllName), TimeSpan.FromSeconds(30));
            for (var i = 1; ; i++)
            {
                try
                {
                    if (kind == InstallKind.SingleFile) Apply(self, target);
                    else ApplyFolder(selfDir, appDir);
                    break;
                }
                catch (Exception e) when (i < 20 && e is IOException or UnauthorizedAccessException) { Thread.Sleep(500); }
            }
        }
        catch (Exception e)
        {
            Log("Failed before anything was replaced: " + e);
            fail($"Couldn't install Survivor's Toolkit {version}: {e.Message}\n\nNothing was changed.");
            return 1;
        }
        try
        {
            // Antivirus sometimes removes a freshly copied, unsigned exe.
            if (!File.Exists(target) || Sha256Of(target) != Sha256Of(self))
                throw new IOException($"{Path.GetFileName(target)} disappeared or changed right after it was copied, which usually means antivirus removed it.");
            if (restart)
            {
                var psi = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
                psi.ArgumentList.Add(UpdatedFromArg);
                psi.ArgumentList.Add(fromVersion);
                Process.Start(psi)?.Dispose();
            }
            Log(restart ? "Installed." : "Installed; it starts the next time the app is opened.");
            return 0;
        }
        catch (Exception e)
        {
            Log("Installed but couldn't be used, putting the old version back: " + e);
            var back = false;
            try
            {
                if (kind == InstallKind.Folder) back = PutBackFolder(selfDir, appDir);
                else if (File.Exists(target + OldSuffix)) { File.Move(target + OldSuffix, target, overwrite: true); back = true; }
                if (back && restart) Process.Start(new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! })?.Dispose();
            }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
            fail($"Survivor's Toolkit {version} was installed, but Windows wouldn't use it: {e.Message}\n\n" +
                (back ? $"Version {fromVersion} was put back." : $"Version {fromVersion} is kept as {Path.GetFileName(target)}{OldSuffix}; rename it to use it again.") +
                " If your antivirus quarantined the new version, allow it there, or download it from " + UpdateCheck.ReleasesPage);
            return 1;
        }
    }

    /// <summary>Waits until nothing has <paramref name="file"/> open. Returns at once when it doesn't exist.</summary>
    public static void WaitUntilFree(string file, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                if (!File.Exists(file)) return;
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= until)
                    throw new TimeoutException($"{Path.GetFileName(file)} is still in use. Close every copy of Survivor's Toolkit and try again.", e);
                Thread.Sleep(250);
            }
        }
    }

    static void WaitForExit(int pid, TimeSpan timeout)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.WaitForExit(timeout)) throw new TimeoutException("The old copy of Survivor's Toolkit didn't close.");
        }
        catch (ArgumentException) { /* already gone */ }
    }

    /// <summary>Where the installer notes what it did, so a failed update can be looked into.</summary>
    public static string LogFile => Path.Combine(AppSettings.Folder, "update.log");

    static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
