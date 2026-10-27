using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// "A new version is out": its change notes, with Update now (download and check it), Release notes (the release page)
/// and Later, and a box to stop reminding about this version. Once the download is checked it offers Restart now or
/// When I close the app, as ModWright does; the main window installs it either way (<see cref="Prepared"/>). A copy
/// installed with the Setup.exe updates through Velopack (<see cref="AppUpdates"/>). A copy that can't replace itself
/// gets Download instead, which saves the new version through the browser.
/// </summary>
sealed class UpdateForm : Form
{
    readonly ReleaseInfo _release;
    readonly FlatProgressBar _bar = new() { Height = 6, Dock = DockStyle.Top, Visible = false, Margin = new Padding(0, 6, 0, 6) };
    readonly Label _status = Muted();
    readonly CheckBox _skip;
    readonly Button _update, _later, _restart, _onClose;
    CancellationTokenSource? _cts;

    /// <summary>True when the box to skip this version was ticked as the window closed.</summary>
    public bool SkipThisVersion => _skip.Checked;
    /// <summary>The downloaded, checked update, or null when it wasn't downloaded.</summary>
    public PendingUpdate? Prepared { get; private set; }
    /// <summary>True when Restart now was pressed; otherwise <see cref="Prepared"/> is installed when the app closes.</summary>
    public bool RestartNow { get; private set; }

    /// <param name="busy">Unsaved ticks or a scan: Restart now still works, but the main window asks about them first.</param>
    public UpdateForm(ReleaseInfo release, bool busy = false)
    {
        _release = release;
        Text = "Update - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(640, 460);
        MinimumSize = new Size(480, 340);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var cannot = AppUpdates.CannotUpdateReason(release);
        var url = AppUpdates.DownloadUrl(release);
        var head = new Label { Text = $"Version {release.Version} is out. You have {UpdateCheck.CurrentVersion}.", AutoSize = true, Font = Theme.HeadingFont, UseMnemonic = false }.Role(ThemeRole.Title);
        var notes = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
            Text = release.Notes.Trim().Length > 0 ? release.Notes.Trim().Replace("\r\n", "\n").Replace("\n", "\r\n") : "No change notes with this release.",
        };
        _skip = new CheckBox { Text = $"Don't remind me about {release.Version}", AutoSize = true };
        _update = Btn("Update now", async (_, _) => await DownloadAsync()).Role(ThemeRole.Primary);
        var download = Btn("Download", (_, _) => Ui.Open(this, url!));
        var page = Btn("Release notes", (_, _) => Ui.Open(this, release.PageUrl));
        _later = Btn("Later", (_, _) => { _cts?.Cancel(); Close(); });
        _restart = Btn("Restart now", (_, _) => { RestartNow = true; Close(); }).Role(ThemeRole.Primary);
        _onClose = Btn("When I close the app", (_, _) => Close());
        _restart.Visible = _onClose.Visible = false;
        _update.Visible = cannot == null;
        download.Visible = cannot != null && url != null;
        _status.Text = cannot != null
            ? cannot + (url != null ? " Download saves the new version through your browser; close this one and run the new one." : " Use Release notes to download it.")
            : AppUpdates.IsInstalled
                ? "Update now downloads only what changed and checks it before anything is changed."
                : "Update now downloads it and checks it against the release's SHA-256 checksums before anything is changed.";
        if (busy && cannot == null) _status.Text += " You have unsaved ticks: you'll be asked about them before it restarts.";
        Tip(_onClose, "Keep working; the new version is installed when this window closes, and starts the next time you open the app");

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(head);
        layout.Controls.Add(notes);
        layout.Controls.Add(_bar);
        _status.MaximumSize = new Size(580, 0);
        layout.Controls.Add(Row(_status));
        layout.Controls.Add(Row(_update, download, _restart, _onClose, page, _later, _skip));
        Controls.Add(layout);
        CancelButton = _later;
        AcceptButton = cannot == null ? _update : null;
        Theme.Apply(this);
        FormClosing += (_, _) => _cts?.Cancel();
    }

    void Tip(Control c, string text)
    {
        var tip = new ToolTip();
        Theme.Style(tip);
        tip.SetToolTip(c, text);
        Disposed += (_, _) => tip.Dispose();
    }

    async Task DownloadAsync()
    {
        _update.Enabled = false;
        _bar.Visible = true;
        _bar.Value = 0;
        _status.ForeColor = Theme.Of(Tone.Muted);
        _status.Text = "Downloading...";
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<int>(p => { if (!IsDisposed) { _bar.Value = p; _status.Text = $"Downloading... {p}%"; } });
            Prepared = await AppUpdates.DownloadAsync(_release, progress, _cts.Token);
            if (IsDisposed) return;
            _bar.Visible = false;
            _status.ForeColor = Theme.Of(Tone.Ok);
            _status.Text = $"Version {_release.Version} is downloaded and checked. Restart now to start using it, or it is installed when you close the app.";
            _update.Visible = _later.Visible = _skip.Visible = false;
            _restart.Visible = _onClose.Visible = true;
            AcceptButton = _restart;
            CancelButton = _onClose;
            _restart.Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (IsDisposed) return;
            _bar.Visible = false;
            _update.Enabled = true;
            _status.Text = "The update failed: " + e.Message + " Nothing was changed.";
            _status.ForeColor = Theme.Of(Tone.Error);
        }
    }
}
