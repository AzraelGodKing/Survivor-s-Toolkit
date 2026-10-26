using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// Scheduled restarts: warn the players in chat, save the world, shut the server down, and - for a server on this PC -
/// start it again and say it is back. The schedule runs while this window is open; the command line's "restart" does
/// the same thing from Windows' own Task Scheduler.
/// </summary>
sealed class RestartsForm : Form
{
    readonly AppSettings _s;
    readonly Action _saveSettings;
    readonly Func<bool> _startServer;

    readonly CheckBox _enabled = new() { Text = "Run the schedule while this window is open", AutoSize = true };
    readonly TextBox _times = new() { Width = 260, PlaceholderText = "05:00, 17:00" };
    readonly TextBox _warn = new() { Width = 180, PlaceholderText = "15, 5, 1" };
    readonly TextBox _message = new() { Width = 520, PlaceholderText = "The server restarts in {minutes} minutes." };
    readonly TextBox _back = new() { Width = 520, PlaceholderText = "The server is back up." };
    readonly CheckBox _save = new() { Text = "Save the world first (saveworld)", AutoSize = true };
    readonly CheckBox _again = new() { Text = "Start the server on this PC again afterwards", AutoSize = true };
    readonly NumericUpDown _afterSeconds = new() { Minimum = 0, Maximum = 3600, Value = 30, Width = 80, AccessibleName = "Seconds before starting again" };
    readonly NumericUpDown _backSeconds = new() { Minimum = 0, Maximum = 3600, Value = 120, Width = 80, AccessibleName = "Seconds before saying it is back" };
    readonly Label _next = Muted();
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), AccessibleName = "What the schedule has done" };
    readonly Button _start, _stop, _now;
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 30000 };

    CancellationTokenSource? _cts;
    bool _filling;

    public RestartsForm(AppSettings s, Action saveSettings, Func<bool> startServer)
    {
        _s = s;
        _saveSettings = saveSettings;
        _startServer = startServer;
        Text = "Scheduled restarts - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(900, 740);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterParent;

        _start = SmallBtn("Start the schedule", (_, _) => StartSchedule()).Role(ThemeRole.Primary);
        _stop = SmallBtn("Stop", (_, _) => StopSchedule("Stopped."));
        _now = SmallBtn("Restart now...", async (_, _) => await RestartNowAsync());

        var plan = Card("The restart", new Control[] { _next },
            Row(_enabled),
            Row(Lbl("At"), _times, Muted("times of day on this PC's clock, separated by commas")),
            Row(Lbl("Warn"), _warn, Muted("minutes before, in chat")),
            Row(Lbl("Warning"), _message),
            Row(Lbl("Back again"), _back),
            Row(Muted("{minutes} is how many are left and {time} the restart's time.")),
            Row(_save),
            Row(_again, Lbl("after"), _afterSeconds, Muted("seconds; say it is back"), _backSeconds, Muted("seconds after that (0 says nothing)")),
            Row(Muted("A hosted server is started again by the host, so leave that off and let their panel bring it back.")));

        var runCard = Card("Running", new Control[] { _start, _stop, _now, SmallBtn("Clear", (_, _) => _log.Clear()) }, _log,
            Row(Muted("The schedule only runs while this window is open. For a restart that happens whether the app is open or not, point Windows' Task Scheduler at:  SurvivorsToolkit restart")));
        runCard.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(plan, 0, 0);
        layout.Controls.Add(runCard, 0, 1);
        Controls.Add(layout);

        foreach (var box in new Control[] { _times, _warn, _message, _back }) box.Leave += (_, _) => Keep();
        foreach (var tick in new[] { _enabled, _save, _again }) tick.CheckedChanged += (_, _) => Keep();
        _afterSeconds.ValueChanged += (_, _) => Keep();
        _backSeconds.ValueChanged += (_, _) => Keep();
        _clock.Tick += (_, _) => ShowNext();
        _clock.Start();
        Fill();
        Theme.Apply(this);
        Shown += (_, _) => { if (_s.Restarts.Enabled) StartSchedule(); };
        FormClosing += (_, e) =>
        {
            if (_cts != null && !Confirm(this, "The schedule is running. Stop it and close?\n\nIt only runs while this window is open.")) { e.Cancel = true; return; }
            Keep();
            _clock.Stop();
            _cts?.Cancel();
        };
    }

    void Fill()
    {
        _filling = true;
        var p = _s.Restarts;
        _enabled.Checked = p.Enabled;
        _times.Text = string.Join(", ", p.Times);
        _warn.Text = string.Join(", ", p.WarnMinutes);
        _message.Text = p.Message;
        _back.Text = p.BackMessage;
        _save.Checked = p.SaveWorld;
        _again.Checked = p.StartAgain;
        _afterSeconds.Value = Math.Clamp(p.StartAgainAfterSeconds, 0, 3600);
        _backSeconds.Value = Math.Clamp(p.BackAfterSeconds, 0, 3600);
        _filling = false;
        ShowNext();
        Busy();
    }

    /// <summary>Saves what is in the boxes to the settings.</summary>
    void Keep()
    {
        if (_filling) return;
        var p = _s.Restarts;
        p.Enabled = _enabled.Checked;
        p.Times = Split(_times.Text).Where(t => Restarts.ReadTime(t) != null).ToList();
        p.WarnMinutes = Split(_warn.Text).Select(t => int.TryParse(t, out var n) ? n : 0).Where(n => n > 0).ToList();
        p.Message = _message.Text.Trim();
        p.BackMessage = _back.Text.Trim();
        p.SaveWorld = _save.Checked;
        p.StartAgain = _again.Checked;
        p.StartAgainAfterSeconds = (int)_afterSeconds.Value;
        p.BackAfterSeconds = (int)_backSeconds.Value;
        _saveSettings();
        ShowNext();
        static IEnumerable<string> Split(string text) =>
            text.Split(',', ';', ' ').Select(t => t.Trim()).Where(t => t.Length > 0);
    }

    void ShowNext()
    {
        var bad = Split(_times.Text).Where(t => Restarts.ReadTime(t) == null).ToList();
        _next.Text = _s.Restarts.NextRun(DateTime.Now) is { } next
            ? $"Next: {next:ddd HH:mm} ({Display.In(next - DateTime.Now)})" + (bad.Count > 0 ? $"  -  couldn't read: {string.Join(", ", bad)}" : "")
            : "No times that can be read, so nothing is scheduled.";
        static IEnumerable<string> Split(string text) =>
            text.Split(',', ';', ' ').Select(t => t.Trim()).Where(t => t.Length > 0);
    }

    void StartSchedule()
    {
        Keep();
        if (_s.Restarts.NextRun(DateTime.Now) == null) { Error(this, "Fill in at least one time, as 05:00."); return; }
        if (!_s.Telnet.IsSet) { Error(this, "Set the server's console up first, in the server panel: the restart is sent over telnet."); return; }
        _cts = new CancellationTokenSource();
        Busy();
        _ = LoopAsync(_cts.Token);
    }

    void StopSchedule(string why)
    {
        _cts?.Cancel();
        _cts = null;
        Busy();
        Log(why);
    }

    /// <summary>Waits for the next restart, does it, then waits for the one after.</summary>
    async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_s.Restarts.NextRun(DateTime.Now) is not { } next) { Log("No times that can be read; the schedule stopped."); break; }
                var steps = _s.Restarts.Timeline(next);
                Log($"Next restart at {next:ddd HH:mm}; first warning at {steps[0].When:HH:mm}.");
                await RunAsync(steps, ct);
                // Past the restart's own minute, so the next one isn't this one again.
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
            }
        }
        catch (OperationCanceledException) { }
        if (!ct.IsCancellationRequested) { _cts = null; Busy(); }
    }

    /// <summary>Does a timeline, step by step, writing each one to the log.</summary>
    async Task RunAsync(List<RestartAction> steps, CancellationToken ct)
    {
        try
        {
            await Restarts.RunAsync(steps, DoAsync, () => DateTime.Now, (wait, c) => Task.Delay(wait, c), ct);
            Log("The restart is done.");
        }
        catch (OperationCanceledException) { throw; }
        catch (ServerConsoleException e) { Log("! " + e.Message + " The rest of this restart was skipped."); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Log("! Couldn't start the server again: " + e.Message);
        }
    }

    /// <summary>
    /// One step. Each console step gets its own link: the shutdown closes the one before it, and the steps are minutes
    /// apart, so there is nothing to keep open in between.
    /// </summary>
    async Task DoAsync(RestartAction step, CancellationToken ct)
    {
        Log(step.Describe);
        if (step.Step == RestartStep.StartAgain)
        {
            if (!_startServer()) Log("! No dedicated server on this PC to start (Options > Dedicated server).");
            return;
        }
        using var console = await TelnetConsole.ConnectAsync(_s.Telnet.Host, _s.Telnet.Port, Secrets.Unprotect(_s.Telnet.ProtectedPassword), ct);
        var answer = await console.SendAsync(step.Command!, ct);
        if (answer.Trim().Length > 0) Log("  " + answer.Replace("\n", "\n  "));
    }

    /// <summary>Restart now, with the same warnings: the first one goes out at once.</summary>
    async Task RestartNowAsync()
    {
        Keep();
        if (!_s.Telnet.IsSet) { Error(this, "Set the server's console up first, in the server panel."); return; }
        var warn = _s.Restarts.WarnMinutes.Where(m => m > 0).DefaultIfEmpty(0).Max();
        var runAt = DateTime.Now.AddMinutes(warn);
        var steps = _s.Restarts.Timeline(runAt);
        if (!Confirm(this, $"Restart the server at {runAt:HH:mm}?\n\n" + string.Join("\n", steps.Select(a => $"{a.When:HH:mm}  {a.Describe}")), MessageBoxIcon.Warning)) return;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts?.Token ?? CancellationToken.None);
        _now.Enabled = false;
        try { await RunAsync(steps, cts.Token); }
        catch (OperationCanceledException) { Log("The restart was stopped part way."); }
        finally { _now.Enabled = true; }
    }

    void Busy()
    {
        var running = _cts != null;
        _start.Enabled = !running;
        _stop.Enabled = running;
        _times.Enabled = _warn.Enabled = !running;
    }

    void Log(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(text)); return; }
        _log.AppendText((_log.TextLength > 0 ? Environment.NewLine : "") + $"{DateTime.Now:HH:mm:ss}  " + text.Replace("\n", Environment.NewLine));
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }
}
