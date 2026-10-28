using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The "Saved login" row in Server sync and the server panel: pick a login you kept and its details fill the boxes
/// in one click; Save keeps what is in the boxes under a name (with the password only if you say so); Forget drops it.
/// </summary>
sealed class SavedLoginBar
{
    readonly AppSettings _s;
    readonly Action _saveSettings;
    readonly bool _telnet;
    readonly IWin32Window _owner;
    readonly Func<string> _host;
    readonly Func<bool> _hasPassword;
    readonly Func<string, bool, SavedLogin?> _save;
    readonly Action<SavedLogin> _use;
    readonly ComboBox _pick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Saved login" };
    readonly Button _forget;
    bool _filling;

    /// <param name="owner">The window, for dialogs.</param>
    /// <param name="telnet">Whether this is the server panel (telnet side) rather than Server sync (FTP side).</param>
    /// <param name="host">The host in the boxes now, to name a new login after.</param>
    /// <param name="hasPassword">Whether the password box has something in it (no point asking to keep an empty one).</param>
    /// <param name="save">Keeps the boxes under the name (and the password when the bool is true); null when there is nothing to keep.</param>
    /// <param name="use">Fills the boxes from a picked login.</param>
    public SavedLoginBar(IWin32Window owner, AppSettings s, Action saveSettings, bool telnet, Func<string> host, Func<bool> hasPassword, Func<string, bool, SavedLogin?> save, Action<SavedLogin> use)
    {
        _owner = owner;
        _s = s;
        _saveSettings = saveSettings;
        _telnet = telnet;
        _host = host;
        _hasPassword = hasPassword;
        _use = use;
        _save = save;

        _pick.SelectedIndexChanged += (_, _) =>
        {
            _forget!.Enabled = _pick.SelectedItem is Item;
            if (_filling || _pick.SelectedItem is not Item i) return;
            _use(i.Login);
            _saveSettings();
        };
        var saveBtn = SmallBtn("Save login...", (_, _) => Save());
        _forget = SmallBtn("Forget", (_, _) => Forget());
        var tip = new ToolTip();
        tip.SetToolTip(_pick, "Pick a login you saved to fill in the boxes");
        tip.SetToolTip(saveBtn, "Keep what is in the boxes under a name, to reuse it with one click");
        tip.SetToolTip(_forget, "Forget the picked login");
        Row = Ui.Row(Lbl("Saved login"), _pick, saveBtn, _forget);
        Fill(null);
    }

    /// <summary>The row to put in the window.</summary>
    public TableLayoutPanel Row { get; }

    /// <summary>Turns the row off while the window is busy.</summary>
    public bool Enabled
    {
        set { Row.Enabled = value; }
    }

    sealed record Item(SavedLogin Login, bool Telnet)
    {
        public override string ToString() => Login.Describe(Telnet);
    }

    string Suggest() => _pick.SelectedItem is Item i ? i.Login.Name : SavedLogins.SuggestName(_host());

    void Fill(SavedLogin? select)
    {
        _filling = true;
        _pick.Items.Clear();
        var logins = SavedLogins.For(_s.Logins, _telnet);
        _pick.Items.Add(logins.Count == 0 ? "(none saved yet)" : "(pick a saved login)");
        foreach (var l in logins) _pick.Items.Add(new Item(l, _telnet));
        _pick.SelectedIndex = select == null ? 0 : Math.Max(0, logins.IndexOf(select) + 1);
        _forget.Enabled = _pick.SelectedItem is Item;
        _filling = false;
    }

    void Save()
    {
        if (Ask(_owner, "Save login", "Name this login, so you can pick it next time instead of typing it again:", Suggest()) is not { Length: > 0 } name) return;
        var answer = !_hasPassword() ? DialogResult.No : MessageBox.Show(_owner,
            "Save the password with this login too?\n\nYes: it is kept encrypted for your Windows account, so the login works with one click.\nNo: only the address and user are kept, and you type the password each time.",
            "Save login", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return;
        if (_save(name, answer == DialogResult.Yes) is not { } login) return;
        _saveSettings();
        Fill(login);
    }

    void Forget()
    {
        if (_pick.SelectedItem is not Item i) return;
        var what = _telnet ? "its server panel details" : "its Server sync details";
        if (!Confirm(_owner, $"Forget the saved login \"{i.Login.Name}\" ({what})?")) return;
        SavedLogins.Forget(_s.Logins, i.Login, _telnet);
        _saveSettings();
        Fill(null);
    }
}
