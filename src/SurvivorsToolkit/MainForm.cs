using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The main window: the brand bar with the tool tabs and the MODS and OPTIONS pages, the page under it and a footer. The logic lives in
/// SurvivorsToolkit.Core; these files only show it and pass clicks on.
/// </summary>
public sealed partial class MainForm : Form
{
    readonly AppSettings _s;
    readonly ToolTip _tip = new() { AutoPopDelay = 20000, InitialDelay = 400, ReshowDelay = 100 };
    readonly Panel _pages = new() { Dock = DockStyle.Fill };
    NavButton _navMods = null!, _navOptions = null!;
    Control _modsPage = null!, _optionsPage = null!;

    public const string Tagline = "Craft your 7d2d experience";
    public static string Version => UpdateCheck.CurrentVersion;

    public MainForm(AppSettings s, string? updatedFrom = null)
    {
        _s = s;
        Text = "Survivor's Toolkit";
        Font = Theme.BodyFont;
        MinimumSize = new Size(960, 640);
        Size = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        AllowDrop = true;
        Theme.Style(_tip);

        _modsPage = BuildModsPage();
        _optionsPage = BuildOptionsPage();
        _pages.Controls.Add(_modsPage);
        _pages.Controls.Add(_optionsPage);
        // Docking goes from the last control added to the first, so the page that fills goes in first.
        Controls.Add(_pages);
        Controls.Add(BuildHeader());
        Controls.Add(BuildFooter());
        RestoreBounds_();
        Theme.Apply(this);
        ShowPage(_modsPage);

        Shown += async (_, _) =>
        {
            if (updatedFrom != null) ShowNote($"Updated from {updatedFrom} to {UpdateCheck.CurrentVersion}.", Tone.Ok);
            await ScanAsync();
            // A crash is the more urgent of the two notes, so it wins the one line there is.
            if (!await NoteLastCrashAsync()) await NoteGameUpdateAsync();
            ShowWhatsNewAfterUpdate(updatedFrom);
            if (_s.CheckForUpdates) await CheckForUpdateAsync(quiet: true);
        };
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true || DroppedLink(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += async (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths) await InstallAsync(paths);
            else if (DroppedLink(e.Data) is { } link) await InstallFromLinkAsync(link);
        };
        KeyDown += OnKey;
        FormClosing += OnClosing;
        // A downloaded update is installed once the window is gone (and started again when it was Restart now).
        FormClosed += (_, _) => InstallPendingUpdate(restart: _restartAfterUpdate);
    }

    void Tip(Control c, string text) => _tip.SetToolTip(c, text);

    /// <summary>A link dragged from a browser (its address bar, or a download link on a page), or null.</summary>
    static string? DroppedLink(IDataObject? data)
    {
        if (data == null) return null;
        foreach (var format in new[] { "UniformResourceLocatorW", DataFormats.UnicodeText, DataFormats.Text })
        {
            if (!data.GetDataPresent(format)) continue;
            var value = data.GetData(format) switch
            {
                string t => t,
                MemoryStream ms => (format == "UniformResourceLocatorW" ? System.Text.Encoding.Unicode : System.Text.Encoding.Default).GetString(ms.ToArray()),
                _ => null,
            };
            var line = value?.Split('\0', '\r', '\n')[0].Trim();
            if (ModDownloader.IsLink(line)) return line;
        }
        return null;
    }

    Control BuildHeader()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 6, RowCount = 1, Padding = new Padding(16, 10, 12, 2) }.Role(ThemeRole.Header);
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 8), Anchor = AnchorStyles.Left };
        title.Controls.Add(new BrandName { Font = Theme.TitleFont, Margin = new Padding(0, 6, 14, 0) }.Role(ThemeRole.Title));
        var tagline = new Label { Text = Tagline.ToUpperInvariant(), AutoSize = true, Margin = new Padding(0, 10, 0, 0), Font = Theme.SmallFont, UseMnemonic = false }.Role(ThemeRole.Muted);
        title.Controls.Add(tagline);
        bar.Controls.Add(title, 0, 0);

        // The tools that have a window of their own sit left of the two pages. They open (or bring back) that window
        // rather than switch the page, so they are never lit up like MODS and OPTIONS are.
        var tools = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        tools.Controls.Add(ToolTab("S&ERVER", "Server panel", "The server panel: status over telnet (day, blood moon, players), the admin console and scheduled restarts", OpenServerConsole));
        tools.Controls.Add(ToolTab("S&YNC", "Server sync", "Server sync: compare your mods with a dedicated server's Mods folder (a folder or FTP) and push the ones it needs", () => _ = OpenServerSyncFromHeader()));
        tools.Controls.Add(ToolTab("S&AVES", "Worlds and saves", "Your worlds and saved games, and backups of them", OpenSaves));
        tools.Controls.Add(ToolTab("&LOG", "Game log", "Read the game's last log: errors and warnings grouped, with the mod most likely at fault", () => { if (HasGame()) OpenLog(watch: false); }));
        tools.Controls.Add(ToolTab("&CRASH", "Crash helper", "What went wrong last time? The last log read down to the mod that most likely broke it", () => { if (HasGame()) _ = OpenCrashHelperAsync(); }));
        tools.Controls.Add(ToolTab("&TWEAKS", "XML tweaks", "Make a small XML tweak mod from a form, with xpaths read from the game's own config", OpenTweaks));
        bar.Controls.Add(tools, 2, 0);
        bar.Controls.Add(new Panel { Width = 1, Height = 18, Anchor = AnchorStyles.Bottom, Margin = new Padding(8, 0, 8, 12) }.Role(ThemeRole.Divider), 3, 0);

        _navMods = new NavButton { Text = "&MODS", AccessibleName = "Mods" };
        _navOptions = new NavButton { Text = "&OPTIONS", AccessibleName = "Options" };
        _navMods.Click += (_, _) => ShowPage(_modsPage);
        _navOptions.Click += (_, _) => ShowPage(_optionsPage);
        _navMods.Anchor = _navOptions.Anchor = AnchorStyles.Bottom;
        bar.Controls.Add(_navMods, 4, 0);
        bar.Controls.Add(_navOptions, 5, 0);

        // On a narrow window the tagline goes first, so the tools never push the pages off the edge.
        var fixedWidth = 0;
        bar.Layout += (_, _) =>
        {
            if (fixedWidth == 0) fixedWidth = title.PreferredSize.Width - tagline.PreferredSize.Width - tagline.Margin.Horizontal;
            var rest = bar.Padding.Horizontal + tools.PreferredSize.Width + _navMods.PreferredSize.Width + _navOptions.PreferredSize.Width + LogicalToDeviceUnits(17);
            var show = bar.ClientSize.Width >= rest + fixedWidth + tagline.PreferredSize.Width + tagline.Margin.Horizontal;
            if (tagline.Visible != show) tagline.Visible = show;
        };
        return bar;
    }

    NavButton ToolTab(string text, string name, string tip, Action open)
    {
        var b = new NavButton { Text = text, AccessibleName = name, AccessibleRole = AccessibleRole.PushButton, Padding = new Padding(10, 8, 10, 10) };
        b.Click += (_, _) => open();
        Tip(b, tip);
        return b;
    }

    /// <summary>Brings back a tool's window when it is open already, so a second click doesn't stack another copy.</summary>
    bool Raise<T>() where T : Form
    {
        if (Application.OpenForms.OfType<T>().FirstOrDefault() is not { } open) return false;
        if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal;
        open.Activate();
        return true;
    }

    /// <summary>The tools that read the game's files need it found first; says so when it isn't.</summary>
    bool HasGame()
    {
        if (_scan != null) return true;
        Error(this, "Find the game first (Game folder... on the MODS page).");
        return false;
    }

    Control BuildFooter()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, RowCount = 1, Padding = new Padding(16, 3, 16, 4) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.Controls.Add(new Label { Text = "Survivor's Toolkit " + Version, AutoSize = true, Font = Theme.SmallFont, Anchor = AnchorStyles.Left, Margin = Padding.Empty }.Role(ThemeRole.Muted), 0, 0);
        _updateReady.Font = Theme.SmallFont;
        _updateReady.Anchor = AnchorStyles.Left;
        _updateReady.LinkClicked += (_, _) => RestartToUpdate();
        bar.Controls.Add(_updateReady, 1, 0);
        bar.Controls.Add(new Label { Text = "Not affiliated with The Fun Pimps or Valve. Drop a mod's .zip, folder or download link anywhere on the window to install it.", AutoSize = true, Font = Theme.SmallFont, Anchor = AnchorStyles.Right, Margin = Padding.Empty }.Role(ThemeRole.Muted), 2, 0);
        var footer = new Panel { Dock = DockStyle.Bottom, AutoSize = true };
        footer.Controls.Add(bar);
        footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1 }.Role(ThemeRole.Divider));
        return footer;
    }

    void ShowPage(Control page)
    {
        foreach (Control p in _pages.Controls) p.Visible = p == page;
        _navMods.Selected = page == _modsPage;
        _navOptions.Selected = page == _optionsPage;
        if (page == _optionsPage) FillOptions();
    }

    /// <summary>Re-colours every window after a theme change.</summary>
    void ApplyTheme()
    {
        foreach (Form f in Application.OpenForms) Theme.Apply(f);
        _checked = (null, new(), default); // the problems list is coloured too
        FillList();
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (!_modsPage.Visible) return;
        if (e.KeyCode == Keys.F5) { _ = ScanAsync(); e.Handled = true; }
        else if (e.Control && e.KeyCode == Keys.F) { _filter.Focus(); e.Handled = e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.S) { _ = SaveAndRescanAsync(); e.Handled = e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.Z && !(ActiveControl is TextBoxBase)) { UndoRedo(undo: true); e.Handled = e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.Y && !(ActiveControl is TextBoxBase)) { UndoRedo(undo: false); e.Handled = e.SuppressKeyPress = true; }
        else if (e.Control && e.KeyCode == Keys.O) { PickArchive(); e.Handled = e.SuppressKeyPress = true; }
    }

    void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var r = MessageBox.Show(this, "You have changes that aren't saved to the game. Save them now?", "Survivor's Toolkit", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) { e.Cancel = true; _restartAfterUpdate = false; return; }
            if (r == DialogResult.Yes && !SaveToGame()) { e.Cancel = true; _restartAfterUpdate = false; return; }
        }
        _s.WindowMaximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _s.WindowBounds = new[] { b.X, b.Y, b.Width, b.Height };
        SaveSettings();
    }

    void RestoreBounds_()
    {
        if (_s.WindowBounds is not { Length: 4 } b) return;
        var r = new Rectangle(b[0], b[1], b[2], b[3]);
        if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = r;
        if (_s.WindowMaximized) WindowState = FormWindowState.Maximized;
    }

    void SaveSettings()
    {
        try { _s.Save(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't save the settings: " + e.Message); }
    }
}
