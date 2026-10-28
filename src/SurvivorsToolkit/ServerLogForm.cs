using System.Collections.Specialized;
using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// Saving the server's log for the whole time the app is open, whichever window is: one per app, so closing the Server
/// log window doesn't stop it. It stops when the app closes (the command line's "serverlog" runs without the app).
/// </summary>
static class ServerLogService
{
    static CancellationTokenSource? _cts;
    static bool _hooked;

    public static ServerLogWriter? Writer { get; private set; }
    public static string Status { get; private set; } = "Not saving the server's log.";
    public static bool Running => _cts != null;

    /// <summary>Starts (or starts again with the settings as they are now). Returns why it can't, or null.</summary>
    public static string? Start(AppSettings s, Func<ServerInstall?> localServer)
    {
        Stop();
        var plan = s.ServerLogs;
        if (!plan.Any) return "Tick errors, warnings or everything first.";
        Func<CancellationToken, Task> follow;
        var writer = new ServerLogWriter(plan.FolderOrDefault, plan);
        if (plan.Source == ServerLogSource.Telnet)
        {
            if (!s.Telnet.IsSet || string.IsNullOrWhiteSpace(s.Telnet.Host)) return "Set up the server's console first: SERVER, then its address, port and password.";
            var t = s.Telnet;
            var (host, port, password, describe) = (t.Host, t.Port, Secrets.Unprotect(t.ProtectedPassword), t.Describe);
            follow = ct => ServerLogs.FollowConsoleAsync(writer,
                async c => await TelnetConsole.ConnectAsync(host, port, password, c), describe, Said, ct);
        }
        else
        {
            if (localServer() is not { } server) return "There is no dedicated server on this PC. Pick its folder in Options > Dedicated server, or save the log from the server's console instead.";
            follow = ct => ServerLogs.FollowFileAsync(writer, () => ServerLogs.NewestLog(server), Said, ct);
        }
        Writer = writer;
        var cts = _cts = new CancellationTokenSource();
        Status = "Starting...";
        _ = Task.Run(() => follow(cts.Token));
        if (!_hooked) { Application.ApplicationExit += (_, _) => Stop(); _hooked = true; }
        return null;
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        Writer?.Dispose();
        Writer = null;
        Status = "Not saving the server's log.";
    }

    static void Said(string status) => Status = status;
}

/// <summary>
/// Server log: saves the server's log - its errors, its warnings, and every line - to three files a day, so a server
/// owner has them when something went wrong overnight, and zips a day of them to send to a mod's author or a friend.
/// The log comes from the dedicated server's log file on this PC, or from the telnet console for a hosted one.
/// </summary>
sealed class ServerLogForm : Form
{
    readonly AppSettings _s;
    readonly Action _saveSettings;
    readonly Func<ServerInstall?> _localServer;

    readonly CheckBox _errors = new() { Text = "Errors", AutoSize = true };
    readonly CheckBox _warnings = new() { Text = "Warnings", AutoSize = true };
    readonly CheckBox _everything = new() { Text = "Everything", AutoSize = true };
    readonly NumericUpDown _keep = new() { Minimum = 0, Maximum = 3650, Value = 14, Width = 80, AccessibleName = "Days to keep" };
    readonly TextBox _folder = new() { Width = 480, Tag = "fill", AccessibleName = "Folder for the log files" };
    readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460, AccessibleName = "Where the log comes from" };
    readonly Label _sourceInfo = Muted();
    readonly CheckBox _startWithApp = new() { Text = "Start saving when Survivor's Toolkit opens", AutoSize = true };
    readonly Label _state = new() { AutoSize = true, UseMnemonic = false, Font = Theme.SectionFont };
    readonly Label _status = Muted();
    readonly Label _counts = Muted();
    readonly ComboBox _days = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, AccessibleName = "Day" };
    readonly Button _startStop;
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    bool _filling;

    public ServerLogForm(AppSettings s, Action saveSettings, Func<ServerInstall?> localServer)
    {
        _s = s;
        _saveSettings = saveSettings;
        _localServer = localServer;
        Text = "Server log - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(900, 640);
        MinimumSize = new Size(760, 520);
        StartPosition = FormStartPosition.CenterParent;

        _source.Items.AddRange(new object[] { "The dedicated server on this PC (its log file)", "The server's console over telnet (works with a hosted server)" });
        var browse = SmallBtn("Browse...", (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Pick where the server's log files go", UseDescriptionForTitle = true, SelectedPath = _s.ServerLogs.FolderOrDefault };
            if (d.ShowDialog(this) == DialogResult.OK) { _folder.Text = d.SelectedPath; Changed(); }
        });
        var useDefault = SmallBtn("Default", (_, _) => { _folder.Text = ServerLogWriter.DefaultFolder; Changed(); });

        var what = Card("What to save", null,
            Row(_errors, _warnings, _everything, Muted("each to its own file:  errors-, warnings- and everything-<date>.log")),
            Row(Lbl("Keep"), _keep, Muted("days of files (today included); older ones are deleted at midnight. 0 keeps them all.")),
            Row(Lbl("Folder"), _folder, browse, useDefault),
            Row(Lbl("From"), _source),
            Row(_sourceInfo),
            Row(_startWithApp));

        _startStop = SmallBtn("Start saving", (_, _) => StartStop()).Role(ThemeRole.Primary);
        var running = Card("Saving", new Control[] { _startStop },
            Row(_state),
            Row(_status),
            Row(_counts),
            Row(Muted("It carries on when this window is closed, and stops when the app does. To save the log with no window open, point Windows' Task Scheduler at:  SurvivorsToolkit serverlog")));

        var share = Card("Share", null,
            Row(Lbl("Day"), _days,
                SmallBtn("Zip it to share", (_, _) => Share(days: 1)).Role(ThemeRole.Accent),
                SmallBtn("Zip the last 7 days", (_, _) => Share(days: 7)),
                SmallBtn("Open its errors", (_, _) => OpenFile(ServerLogKind.Errors)),
                SmallBtn("Open everything", (_, _) => OpenFile(ServerLogKind.Everything)),
                SmallBtn("Open the folder", (_, _) => { Directory.CreateDirectory(_s.ServerLogs.FolderOrDefault); Ui.Open(this, _s.ServerLogs.FolderOrDefault); })),
            Row(Muted("The zip is put on the clipboard as a file, so you can paste it straight into Discord or an email.")));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), AutoScroll = true };
        layout.Controls.Add(what, 0, 0);
        layout.Controls.Add(running, 0, 1);
        layout.Controls.Add(share, 0, 2);
        Controls.Add(layout);

        foreach (var tick in new[] { _errors, _warnings, _everything, _startWithApp }) tick.CheckedChanged += (_, _) => Changed();
        _keep.ValueChanged += (_, _) => Changed();
        _source.SelectedIndexChanged += (_, _) => Changed();
        _folder.Leave += (_, _) => Changed();
        _timer.Tick += (_, _) => ShowState();
        _timer.Start();
        Fill();
        Theme.Apply(this);
        FormClosing += (_, _) => { Keep(); _timer.Stop(); };
    }

    void Fill()
    {
        _filling = true;
        var p = _s.ServerLogs;
        _errors.Checked = p.Errors;
        _warnings.Checked = p.Warnings;
        _everything.Checked = p.Everything;
        _keep.Value = Math.Clamp(p.KeepDays, 0, 3650);
        _folder.Text = p.FolderOrDefault;
        _source.SelectedIndex = p.Source == ServerLogSource.Telnet ? 1 : 0;
        _startWithApp.Checked = p.StartWithApp;
        _filling = false;
        ShowSource();
        FillDays();
        ShowState();
    }

    /// <summary>Saves what is in the form to the settings.</summary>
    void Keep()
    {
        if (_filling) return;
        var p = _s.ServerLogs;
        p.Errors = _errors.Checked;
        p.Warnings = _warnings.Checked;
        p.Everything = _everything.Checked;
        p.KeepDays = (int)_keep.Value;
        var folder = _folder.Text.Trim();
        p.Folder = folder.Length == 0 || string.Equals(folder, ServerLogWriter.DefaultFolder, StringComparison.OrdinalIgnoreCase) ? null : folder;
        p.Source = _source.SelectedIndex == 1 ? ServerLogSource.Telnet : ServerLogSource.ThisPc;
        p.StartWithApp = _startWithApp.Checked;
        _saveSettings();
    }

    /// <summary>A change takes effect at once: saving starts again with it when it is running.</summary>
    void Changed()
    {
        if (_filling) return;
        Keep();
        ShowSource();
        FillDays();
        if (ServerLogService.Running && ServerLogService.Start(_s, _localServer) is { } why) Error(this, why);
        ShowState();
    }

    void ShowSource()
    {
        if (_source.SelectedIndex == 1)
            _sourceInfo.Text = _s.Telnet.IsSet && _s.Telnet.Host.Trim().Length > 0
                ? $"The console at {_s.Telnet.Describe}, as set up in the server panel (SERVER). It prints the server's log as it happens."
                : "The server's console isn't set up yet: open SERVER and fill in its address, port and password.";
        else
            _sourceInfo.Text = _localServer() is { } server
                ? $"The newest output_log in {server.Folder}. A new file each time the server starts is followed too."
                : "No dedicated server was found on this PC (Options > Dedicated server). For a hosted server, use its console.";
    }

    void StartStop()
    {
        if (ServerLogService.Running) ServerLogService.Stop();
        else
        {
            Keep();
            if (ServerLogService.Start(_s, _localServer) is { } why) Error(this, why);
        }
        ShowState();
    }

    void ShowState()
    {
        var on = ServerLogService.Running;
        _state.Text = on ? "Saving the server's log" : "Not saving";
        _state.ForeColor = Theme.Of(on ? Tone.Ok : Tone.Muted);
        _startStop.Text = on ? "Stop" : "Start saving";
        _status.Text = on ? ServerLogService.Status : "";
        _counts.Text = ServerLogService.Writer is { } w && w.Lines > 0
            ? $"Today: {w.Errors} error{(w.Errors == 1 ? "" : "s")}, {w.Warnings} warning{(w.Warnings == 1 ? "" : "s")}, {w.Lines} line{(w.Lines == 1 ? "" : "s")} in all."
            : "";
        // Midnight, or the first line of today: the list of days gains one.
        if (on && _days.Items.Count == 0 && ServerLogService.Writer?.Lines > 0) FillDays();
    }

    sealed record DayItem(DateTime Day)
    {
        public override string ToString() => Day == DateTime.Today ? $"Today ({Day:d MMM yyyy})" : Day == DateTime.Today.AddDays(-1) ? $"Yesterday ({Day:d MMM yyyy})" : Day.ToString("ddd d MMM yyyy");
    }

    void FillDays()
    {
        var picked = (_days.SelectedItem as DayItem)?.Day;
        _days.BeginUpdate();
        _days.Items.Clear();
        try
        {
            foreach (var (day, _) in ServerLogWriter.Files(_s.ServerLogs.FolderOrDefault)) _days.Items.Add(new DayItem(day));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        _days.EndUpdate();
        var again = _days.Items.Cast<DayItem>().FirstOrDefault(d => d.Day == picked);
        if (again != null) _days.SelectedItem = again;
        else if (_days.Items.Count > 0) _days.SelectedIndex = 0;
    }

    void Share(int days)
    {
        FillDays();
        if (_days.SelectedItem is not DayItem item) { Error(this, "There are no server log files yet. Start saving, and they appear here once the server writes to its log."); return; }
        try
        {
            var zip = ServerLogWriter.Zip(_s.ServerLogs.FolderOrDefault, Enumerable.Range(0, days).Select(i => item.Day.AddDays(-i)));
            try { Clipboard.SetFileDropList(new StringCollection { zip }); }
            catch (System.Runtime.InteropServices.ExternalException) { }
            try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{zip}\""); }
            catch (System.ComponentModel.Win32Exception) { }
            Info(this, $"Zipped to {zip}\n\nIt is on the clipboard: paste it into Discord, an email or a folder.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't zip the logs: " + e.Message); }
    }

    void OpenFile(ServerLogKind kind)
    {
        FillDays();
        if (_days.SelectedItem is not DayItem item) { Error(this, "There are no server log files yet."); return; }
        var path = Path.Combine(_s.ServerLogs.FolderOrDefault, ServerLogWriter.FileName(kind, item.Day));
        if (File.Exists(path)) Ui.Open(this, path);
        else Error(this, kind == ServerLogKind.Errors ? "No errors were saved that day." : $"There is no {ServerLogWriter.FileName(kind, item.Day)} (is \"{kind}\" ticked?).");
    }
}
