using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The XML tweak builder: pick one of the game's own config files, find the item, block or entity in it, pick the
/// property and type a new value. The things, the properties and the values they have now are read from the game's
/// Data/Config, so the xpath is built from the real document instead of being guessed. What comes out is an ordinary
/// mod folder with a Config file of &lt;set&gt; patches.
/// </summary>
sealed class TweakForm : Form
{
    readonly GameInstall _install;
    readonly Func<Task> _rescan;

    readonly ComboBox _file = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, AccessibleName = "Config file" };
    readonly TextBox _findThing = new() { Width = 240, PlaceholderText = "find an item, block or entity", AccessibleName = "Find a thing" };
    readonly ListBox _things = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Things in the file" };
    readonly CheckBox _everyThing = new() { Text = "Every one of them in this file", AutoSize = true };
    readonly ListBox _properties = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Properties" };
    readonly TextBox _value = new() { Width = 200, AccessibleName = "New value" };
    readonly Label _was = Muted();
    readonly ListBox _tweaks = new() { Dock = DockStyle.Fill, IntegralHeight = false, AccessibleName = "Tweaks to make" };
    readonly TextBox _preview = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f), AccessibleName = "The mod's XML" };
    readonly TextBox _name = new() { Width = 240, Text = "My tweaks", AccessibleName = "Mod name" };
    readonly Label _status = Muted();
    readonly Button _add, _remove, _build;

    List<TweakThing> _all = new();
    string _root = "";
    readonly List<Tweak> _list = new();

    public TweakForm(GameInstall install, Func<Task> rescan)
    {
        _install = install;
        _rescan = rescan;
        Text = "XML tweaks - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(1120, 820);
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterParent;

        _add = SmallBtn("Add this tweak", (_, _) => Add()).Role(ThemeRole.Accent);
        _remove = SmallBtn("Remove", (_, _) => Remove());
        _build = SmallBtn("Make the mod", async (_, _) => await BuildAsync()).Role(ThemeRole.Primary);

        _file.SelectedIndexChanged += (_, _) => LoadFile();
        _findThing.TextChanged += (_, _) => FillThings();
        _things.SelectedIndexChanged += (_, _) => FillProperties();
        _properties.SelectedIndexChanged += (_, _) => ShowWas();
        _everyThing.CheckedChanged += (_, _) => ShowWas();
        _value.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; Add(); } };

        var pick = Card("What to change", new Control[] { _status },
            Row(Lbl("Config file"), _file, Muted(install.ConfigFolder)),
            Row(_findThing, _everyThing));
        var thingsCard = Card("Items, blocks, entities", null, _things);
        thingsCard.Dock = DockStyle.Fill;
        var propsCard = Card("Properties", null, _properties, Row(Lbl("New value"), _value, _add), Row(_was));
        propsCard.Dock = DockStyle.Fill;

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        middle.Controls.Add(thingsCard, 0, 0);
        middle.Controls.Add(propsCard, 1, 0);

        var tweaksCard = Card("The tweaks", new Control[] { _remove }, _tweaks);
        tweaksCard.Dock = DockStyle.Fill;
        var previewCard = Card("The mod's XML", new Control[] { Lbl("Mod name"), _name, _build }, _preview);
        previewCard.Dock = DockStyle.Fill;
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        bottom.Controls.Add(tweaksCard, 0, 0);
        bottom.Controls.Add(previewCard, 1, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.Controls.Add(pick, 0, 0);
        layout.Controls.Add(middle, 0, 1);
        layout.Controls.Add(bottom, 0, 2);
        Controls.Add(layout);
        Theme.Apply(this);
        Shown += (_, _) => FillFiles();
    }

    void FillFiles()
    {
        var files = Tweaks.ConfigFiles(_install);
        _file.Items.Clear();
        if (files.Count == 0)
        {
            _status.Text = $"No config XML in {_install.ConfigFolder}. Pick the game folder in Options.";
            _add.Enabled = _build.Enabled = false;
            return;
        }
        _file.Items.AddRange(files.Cast<object>().ToArray());
        // The files people tweak most, when they are there.
        var first = files.FindIndex(f => string.Equals(f, "items.xml", StringComparison.OrdinalIgnoreCase));
        _file.SelectedIndex = first >= 0 ? first : 0;
    }

    void LoadFile()
    {
        if (_file.SelectedItem is not string file) return;
        try
        {
            UseWaitCursor = true;
            var text = File.ReadAllText(Path.Combine(_install.ConfigFolder, file));
            _root = Tweaks.Root(text);
            _all = Tweaks.Things(text);
            _status.Text = $"{_all.Count} named thing{(_all.Count == 1 ? "" : "s")} in {file} (root <{_root}>)";
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            _all = new List<TweakThing>();
            _root = "";
            _status.Text = e.Message;
        }
        finally { UseWaitCursor = false; }
        FillThings();
    }

    void FillThings()
    {
        var find = _findThing.Text.Trim();
        var shown = (find.Length == 0 ? _all : _all.Where(t => t.Name.Contains(find, StringComparison.OrdinalIgnoreCase)))
            .Take(2000).ToList();
        _things.BeginUpdate();
        _things.Items.Clear();
        foreach (var t in shown) _things.Items.Add(t);
        _things.DisplayMember = nameof(TweakThing.Name);
        _things.EndUpdate();
        if (_things.Items.Count > 0) _things.SelectedIndex = 0;
        else FillProperties();
    }

    TweakThing? Picked() => _things.SelectedItem as TweakThing;

    void FillProperties()
    {
        _properties.BeginUpdate();
        _properties.Items.Clear();
        if (Picked() is { } thing)
        {
            foreach (var (name, value) in thing.Properties) _properties.Items.Add($"{name} = {value}");
            foreach (var (name, value) in thing.Attributes) _properties.Items.Add($"@{name} = {value}");
        }
        _properties.EndUpdate();
        if (_properties.Items.Count > 0) _properties.SelectedIndex = 0;
        ShowWas();
    }

    /// <summary>The property picked, as its name, whether it is an attribute, and the value the game has now.</summary>
    (string Name, bool Attribute, string Value)? PickedProperty()
    {
        if (Picked() is not { } thing || _properties.SelectedIndex < 0) return null;
        var i = _properties.SelectedIndex;
        if (i < thing.Properties.Count) return (thing.Properties[i].Name, false, thing.Properties[i].Value);
        var a = thing.Attributes[i - thing.Properties.Count];
        return (a.Name, true, a.Value);
    }

    void ShowWas()
    {
        if (PickedProperty() is not { } p) { _was.Text = ""; _add.Enabled = false; return; }
        _was.Text = _everyThing.Checked
            ? $"Every {Picked()!.Element} in the file gets {p.Name}; this one has {p.Value} now."
            : $"{Picked()!.Name} has {p.Name} = {p.Value} now.";
        _add.Enabled = _root.Length > 0;
        if (_value.TextLength == 0) _value.Text = p.Value;
    }

    void Add()
    {
        if (_file.SelectedItem is not string file || Picked() is not { } thing || PickedProperty() is not { } p) { Error(this, "Pick a thing and a property first."); return; }
        var value = _value.Text.Trim();
        if (value.Length == 0) { Error(this, "Type the value it should have."); return; }
        var name = _everyThing.Checked ? null : thing.Name;
        var was = _everyThing.Checked ? null : p.Value;
        var tweak = p.Attribute
            ? Tweaks.SetAttribute(file, _root, thing.Element, name, p.Name, value, was)
            : Tweaks.SetProperty(file, _root, thing.Element, name, p.Name, value, was);
        var already = _list.FindIndex(t => t.Xpath == tweak.Xpath && string.Equals(t.File, tweak.File, StringComparison.OrdinalIgnoreCase));
        if (already >= 0) _list[already] = tweak;
        else _list.Add(tweak);
        FillTweaks();
    }

    void Remove()
    {
        if (_tweaks.SelectedIndex < 0) return;
        _list.RemoveAt(_tweaks.SelectedIndex);
        FillTweaks();
    }

    void FillTweaks()
    {
        var picked = _tweaks.SelectedIndex;
        _tweaks.BeginUpdate();
        _tweaks.Items.Clear();
        foreach (var t in _list) _tweaks.Items.Add($"{t.File}  -  {t.Describe}");
        _tweaks.EndUpdate();
        if (_tweaks.Items.Count > 0) _tweaks.SelectedIndex = Math.Clamp(picked, 0, _tweaks.Items.Count - 1);
        _preview.Text = string.Join(Environment.NewLine + Environment.NewLine,
            _list.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(f => $"Config/{f}" + Environment.NewLine + Tweaks.Patch(f, _list)));
        _remove.Enabled = _list.Count > 0;
        _build.Enabled = _list.Count > 0;
    }

    async Task BuildAsync()
    {
        if (_list.Count == 0) { Error(this, "Add a tweak first."); return; }
        var name = _name.Text.Trim();
        if (name.Length == 0) { Error(this, "Give the mod a name."); return; }
        var into = Path.Combine(_install.GameMods);
        if (!Confirm(this, $"Make the mod \"{name}\" in {into}?\n\n{_list.Count} tweak{(_list.Count == 1 ? "" : "s")} in {_list.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count()} file{(_list.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? "" : "s")}. It is turned on as soon as it is there, so start the game to try it.")) return;
        try
        {
            Directory.CreateDirectory(into);
            var folder = Tweaks.Build(into, name, "1.0", Environment.UserName, _list);
            _status.Text = "Made " + folder;
            await _rescan();
            if (Confirm(this, $"Made {folder}.\n\nOpen its folder?")) Ui.Open(this, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { Error(this, "Couldn't make the mod: " + e.Message); }
    }
}
