using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The crash helper: what the last log says about how the game ended, the errors in it, and the mod most of them point
/// at - with the mod one click away in the list, so a crash can be answered by turning something off.
/// </summary>
sealed class CrashForm : Form
{
    readonly CrashReport _report;
    readonly ListView _errors = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, ShowItemToolTips = true, AccessibleName = "Errors in the log" };
    readonly TextBox _detail = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), AccessibleName = "The error, as the log has it" };

    public CrashForm(CrashReport report, LibraryScan scan, Action openLog, Action<string> selectMod)
    {
        _report = report;
        Text = "What went wrong - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(1000, 720);
        MinimumSize = new Size(780, 520);
        StartPosition = FormStartPosition.CenterParent;

        string? Name(string id) => scan.Mods.FirstOrDefault(m => m.Id == id)?.DisplayName;
        var summary = new Label
        {
            Text = Crash.Describe(report, Name), AutoSize = true, MaximumSize = new Size(900, 0), UseMnemonic = false,
            ForeColor = Theme.Of(report.Crashed ? Tone.Error : report.Errors.Count > 0 ? Tone.Warn : Tone.Ok),
        };
        var reason = Muted(report.Reason ?? "");
        reason.MaximumSize = new Size(900, 0);

        var suspect = report.SuspectModId;
        var turnOff = Btn("Show me that mod", (_, _) => { if (suspect != null) { selectMod(suspect); Close(); } });
        turnOff.Enabled = suspect != null && Name(suspect) != null;
        turnOff.Role(ThemeRole.Accent);
        var readLog = Btn("Read the whole log", (_, _) => { openLog(); Close(); });
        var openFile = Btn("Open the log file", (_, _) => { if (report.LogPath != null) Ui.Open(this, report.LogPath); });
        openFile.Enabled = report.LogPath != null;
        var bisect = Muted(report.Errors.Count > 0 && suspect == null ? "No mod of yours is named in the errors. Tools > Find the broken mod for me tries them in halves instead." : "");
        bisect.MaximumSize = new Size(900, 0);

        var top = Card("The last session", new Control[] { turnOff, readLog, openFile },
            Row(summary), Row(reason), Row(bisect),
            Row(Muted(report.LogPath ?? "")));

        foreach (var (name, w) in new[] { ("", 60), ("Times", 60), ("Error", 520), ("Mod", 200), ("Why", 220) })
            _errors.Columns.Add(name, (int)(w * Theme.TextScale));
        _errors.SelectedIndexChanged += (_, _) => _detail.Text = (_errors.SelectedItems.Count > 0 ? _errors.SelectedItems[0].Tag as LogGroup : null)?.Detail ?? "";
        foreach (var g in report.Errors)
            _errors.Items.Add(new ListViewItem(new[] { "ERR", g.Count.ToString(), g.Message, g.ModId is { } id ? Name(id) ?? id : "", g.Why ?? "" })
            {
                Tag = g, ToolTipText = g.Message, ForeColor = Theme.Of(Tone.Error),
            });
        var errorsCard = Card("Errors in the log", null, _errors);
        errorsCard.Dock = DockStyle.Fill;
        var detailCard = Card("As the log has it", null, _detail);
        detailCard.Dock = DockStyle.Fill;
        if (report.Errors.Count == 0) _detail.Text = string.Join(Environment.NewLine, report.Tail);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        split.Controls.Add(errorsCard, 0, 0);
        split.Controls.Add(detailCard, 0, 1);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(split, 0, 1);
        Controls.Add(layout);
        Theme.Apply(this);
        if (_errors.Items.Count > 0) _errors.Items[0].Selected = true;
    }
}
