using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The server panel: whether the server is up, who is on it, the day and the blood moon countdown, over the game's
/// telnet console - and an admin console under it, so commands like say, kick, saveworld and shutdown need no terminal.
/// The server can be the one on this PC or one at a game host; all it needs is TelnetEnabled and the port open.
/// </summary>
sealed class ServerConsoleForm : Form
{
    readonly AppSettings _s;
    readonly Action _saveSettings;
    readonly Func<string?> _localConfig;

    readonly TextBox _host = new() { Width = 220, PlaceholderText = "127.0.0.1 or your host's address" };
    readonly NumericUpDown _port = new() { Minimum = 1, Maximum = 65535, Value = 8081, Width = 90 };
    readonly TextBox _password = new() { Width = 180, UseSystemPasswordChar = true, AccessibleName = "Telnet password" };
    readonly NumericUpDown _bloodMoon = new() { Minimum = 0, Maximum = 365, Value = 7, Width = 70, AccessibleName = "Blood moon every" };
    readonly CheckBox _auto = new() { Text = "Refresh every 15 seconds", AutoSize = true, Checked = true };
    readonly Button _connect, _disconnect, _refresh, _fromConfig, _send;

    readonly Label _state = new() { AutoSize = true, UseMnemonic = false, Font = Theme.SectionFont };
    readonly Label _players = Muted();
    readonly Label _clock = Muted();
    readonly Label _horde = Muted();
    readonly Label _version = Muted();
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Players on the server" };
    readonly TextBox _out = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), AccessibleName = "Console output" };
    readonly TextBox _command = new() { Width = 420, PlaceholderText = "a console command, for example  help  or  gettime", Tag = "fill", AccessibleName = "Command" };

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 15000 };
    readonly List<string> _history = new();
    int _historyAt = -1;

    IServerConsole? _console;
    bool _busy, _filling;

    public ServerConsoleForm(AppSettings s, Action saveSettings, Func<string?> localConfig)
    {
        _s = s;
        _saveSettings = saveSettings;
        _localConfig = localConfig;
        Text = "Server panel - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(1040, 820);
        MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterParent;

        _connect = SmallBtn("Connect", async (_, _) => await ConnectAsync()).Role(ThemeRole.Primary);
        _disconnect = SmallBtn("Disconnect", (_, _) => Close_(said: "Disconnected."));
        _refresh = SmallBtn("Refresh", async (_, _) => await RefreshAsync());
        _fromConfig = SmallBtn("Read the server's config", (_, _) => FromLocalConfig());
        _send = SmallBtn("Send", async (_, _) => await SendTypedAsync()).Role(ThemeRole.Accent);

        var setup = Card("Console", new Control[] { _connect, _disconnect },
            Row(Lbl("Address"), _host, Lbl("Port"), _port, Lbl("Password"), _password, _fromConfig),
            Row(Lbl("Blood moon every"), _bloodMoon, Muted("days (BloodMoonFrequency); 0 turns the countdown off"), _auto),
            Row(Muted("The server needs TelnetEnabled true, a TelnetPassword and its TelnetPort reachable. The password is kept encrypted for your Windows account. A host's panel usually lists the telnet port under its console or RCON settings.")));

        foreach (var (name, w) in new[] { ("Player", 220), ("Health", 70), ("Deaths", 70), ("Zombies", 80), ("Level", 70), ("Ping", 70) })
            _list.Columns.Add(name, (int)(w * Theme.TextScale));
        _list.ContextMenuStrip = new ContextMenuStrip();
        _list.ContextMenuStrip.Opening += (_, e) => { if (Picked() == null) e.Cancel = true; else FillPlayerMenu(); };
        var statusCard = Card("Status", new Control[] { _state, _refresh },
            Row(Lbl("Players"), _players, Lbl("   Time"), _clock, Lbl("   Blood moon"), _horde),
            Row(Lbl("Game"), _version),
            _list);
        statusCard.Dock = DockStyle.Fill;
        _list.Dock = DockStyle.Fill;

        _command.KeyDown += OnCommandKey;
        var consoleCard = Card("Admin console", new Control[] { SmallBtn("Say...", async (_, _) => await SayAsync()), SmallBtn("Save the world", async (_, _) => await RunAsync("saveworld")), SmallBtn("Shut down...", async (_, _) => await RunAsync("shutdown")), SmallBtn("Commands", async (_, _) => await RunAsync("help")), SmallBtn("Clear", (_, _) => _out.Clear()) },
            _out, Row(_command, _send));
        consoleCard.Dock = DockStyle.Fill;
        _out.Dock = DockStyle.Fill;

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        split.Controls.Add(statusCard, 0, 0);
        split.Controls.Add(consoleCard, 0, 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(setup, 0, 0);
        layout.Controls.Add(split, 0, 1);
        Controls.Add(layout);

        _timer.Tick += async (_, _) => { if (_console != null && !_busy) await RefreshAsync(quiet: true); };
        _auto.CheckedChanged += (_, _) => _timer.Enabled = _auto.Checked && _console != null;
        Fill();
        Offline("Not connected.");
        Theme.Apply(this);
        FormClosing += (_, _) => { Keep(); _timer.Stop(); _console?.Dispose(); _console = null; };
    }

    void Fill()
    {
        _filling = true;
        var t = _s.Telnet;
        _host.Text = t.Host;
        _port.Value = Math.Clamp(t.Port, 1, 65535);
        _password.Text = Secrets.Unprotect(t.ProtectedPassword);
        _bloodMoon.Value = Math.Clamp(t.BloodMoonFrequency, 0, 365);
        _filling = false;
        if (_host.Text.Trim().Length == 0) FromLocalConfig(quiet: true);
    }

    /// <summary>Saves what is in the boxes to the settings.</summary>
    void Keep()
    {
        if (_filling) return;
        var t = _s.Telnet;
        t.Host = _host.Text.Trim();
        t.Port = (int)_port.Value;
        t.ProtectedPassword = Secrets.Protect(_password.Text);
        t.BloodMoonFrequency = (int)_bloodMoon.Value;
        _saveSettings();
    }

    /// <summary>Fills the boxes from the dedicated server's own serverconfig.xml, when there is one on this PC.</summary>
    void FromLocalConfig(bool quiet = false)
    {
        var path = _localConfig();
        if (path == null || !File.Exists(path))
        {
            if (!quiet) Error(this, "There is no dedicated server config on this PC to read. Fill the address, port and password in by hand, or pick the server folder in Options.");
            return;
        }
        try
        {
            var p = DedicatedServer.ReadConfig(path).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase);
            string? V(string k) => p.TryGetValue(k, out var v) && v.Length > 0 ? v : null;
            _filling = true;
            if (_host.Text.Trim().Length == 0) _host.Text = "127.0.0.1";
            if (V("TelnetPort") is { } port && int.TryParse(port, out var n) && n is > 0 and < 65536) _port.Value = n;
            if (V("TelnetPassword") is { } pw) _password.Text = pw;
            if (V("BloodMoonFrequency") is { } freq && int.TryParse(freq, out var f) && f is >= 0 and <= 365) _bloodMoon.Value = f;
            var off = V("TelnetEnabled")?.Equals("false", StringComparison.OrdinalIgnoreCase) == true;
            _filling = false;
            Keep();
            var note = $"Read {Path.GetFileName(path)}." + (off ? " TelnetEnabled is false in it, so the console is turned off: set it to true and restart the server." : "");
            if (!quiet) { if (_console == null) Offline(note); else Write("# " + note); }
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            _filling = false;
            if (!quiet) Error(this, "Couldn't read the server's config: " + e.Message);
        }
    }

    async Task ConnectAsync()
    {
        Keep();
        if (!_s.Telnet.IsSet) { Error(this, "Fill in the server's address and telnet port first."); return; }
        _console?.Dispose();
        _console = null;
        Busy(true);
        Offline($"Connecting to {_s.Telnet.Describe}...");
        try
        {
            var (host, port, password) = (_host.Text.Trim(), (int)_port.Value, _password.Text);
            _console = await TelnetConsole.ConnectAsync(host, port, password);
            Write(_console.Banner);
            Busy(false);
            await RefreshAsync();
            _timer.Enabled = _auto.Checked;
        }
        catch (ServerConsoleException e)
        {
            Busy(false);
            Offline(e.Message);
            Write("! " + e.Message);
        }
    }

    async Task RefreshAsync(bool quiet = false)
    {
        if (_console == null) return;
        Busy(true);
        try
        {
            var status = await ServerStatusReader.QueryAsync(_console, (int)_bloodMoon.Value);
            _state.Text = "Up";
            _state.ForeColor = Theme.Of(Tone.Ok);
            _players.Text = status.Players.ToString();
            _clock.Text = status.Clock;
            _horde.Text = status.BloodMoon;
            _version.Text = status.Version ?? "?";
            FillPlayers(status.PlayerList);
        }
        catch (ServerConsoleException e)
        {
            _timer.Stop();
            _console.Dispose();
            _console = null;
            Offline(e.Message);
            if (!quiet) Write("! " + e.Message);
        }
        finally { Busy(false); }
    }

    void FillPlayers(List<PlayerOnServer> players)
    {
        _list.BeginUpdate();
        var picked = Picked()?.Id;
        _list.Items.Clear();
        foreach (var p in players)
        {
            var item = new ListViewItem(new[] { p.Name, Text_(p.Health), Text_(p.Deaths), Text_(p.Zombies), Text_(p.Level), Text_(p.Ping) }) { Tag = p };
            if (p.Id == picked) item.Selected = true;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        static string Text_(int? n) => n?.ToString() ?? "";
    }

    PlayerOnServer? Picked() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as PlayerOnServer : null;

    void FillPlayerMenu()
    {
        var menu = _list.ContextMenuStrip!;
        menu.Items.Clear();
        if (Picked() is not { } p) return;
        menu.Items.Add($"Say hello to {p.Name}", null, async (_, _) => await SayAsync($"Hi {p.Name}"));
        menu.Items.Add("Kick...", null, async (_, _) =>
        {
            var reason = Ask(this, "Kick " + p.Name, $"Why is {p.Name} being kicked? They see this. Leave it empty for no reason.");
            if (reason == null) return;
            await RunAsync(ServerCommands.Kick(p.Id.ToString(), reason));
        });
        menu.Items.Add("Ban for a day...", null, async (_, _) =>
        {
            var reason = Ask(this, "Ban " + p.Name, $"Why is {p.Name} being banned for a day? They see this.");
            if (reason == null) return;
            await RunAsync(ServerCommands.Ban(p.Id.ToString(), "1 day", reason));
        });
        Theme.StyleMenu(menu);
    }

    async Task SayAsync(string? message = null)
    {
        message ??= Ask(this, "Say something to the server", "Everyone on the server sees this in chat.");
        if (string.IsNullOrWhiteSpace(message)) return;
        await RunAsync(ServerCommands.Say(message));
    }

    async Task SendTypedAsync()
    {
        var command = _command.Text.Trim();
        if (command.Length == 0) return;
        _command.Clear();
        _history.Remove(command);
        _history.Add(command);
        _historyAt = -1;
        await RunAsync(command);
    }

    /// <summary>Sends one command, after asking when it can't be taken back, and writes the answer to the console.</summary>
    async Task RunAsync(string command)
    {
        if (_console == null) { Error(this, "Connect to the server's console first."); return; }
        if (ServerCommands.NeedsConfirming(command)
            && !Confirm(this, $"Send this to the server?\n\n{command}\n\nIt can't be taken back.", MessageBoxIcon.Warning)) return;
        Busy(true);
        Write("> " + command);
        try
        {
            var answer = await _console.SendAsync(command, CancellationToken.None);
            Write(answer.Length == 0 ? "(no answer)" : answer);
            Busy(false);
            // A command that changes who is on or what time it is: show it right away.
            if (command.Split(' ')[0].ToLowerInvariant() is "kick" or "ban" or "settime" or "say" or "saveworld") await RefreshAsync(quiet: true);
        }
        catch (ServerConsoleException e)
        {
            _timer.Stop();
            _console.Dispose();
            _console = null;
            Offline(e.Message);
            Write("! " + e.Message);
            Busy(false);
        }
    }

    void OnCommandKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; _ = SendTypedAsync(); }
        else if (e.KeyCode is Keys.Up or Keys.Down && _history.Count > 0)
        {
            e.Handled = e.SuppressKeyPress = true;
            if (_historyAt < 0) _historyAt = _history.Count;
            _historyAt = Math.Clamp(_historyAt + (e.KeyCode == Keys.Up ? -1 : 1), 0, _history.Count);
            _command.Text = _historyAt < _history.Count ? _history[_historyAt] : "";
            _command.SelectionStart = _command.TextLength;
        }
    }

    void Write(string text)
    {
        if (text.Length == 0) return;
        _out.AppendText((_out.TextLength > 0 ? Environment.NewLine : "") + text.Replace("\n", Environment.NewLine));
        _out.SelectionStart = _out.TextLength;
        _out.ScrollToCaret();
    }

    /// <summary>Shows that there is no link (and why), and empties the status.</summary>
    void Offline(string why)
    {
        _state.Text = "Not connected" + (why.Length > 0 ? "  -  " + why : "");
        _state.ForeColor = Theme.Of(Tone.Warn);
        _players.Text = _clock.Text = _horde.Text = _version.Text = "?";
        _list.Items.Clear();
    }

    void Close_(string said)
    {
        _timer.Stop();
        _console?.Dispose();
        _console = null;
        Offline(said);
    }

    void Busy(bool busy)
    {
        _busy = busy;
        _connect.Enabled = !busy;
        _disconnect.Enabled = _refresh.Enabled = _send.Enabled = !busy && _console != null;
        _fromConfig.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
