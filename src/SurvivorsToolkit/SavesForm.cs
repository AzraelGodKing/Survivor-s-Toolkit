using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The worlds and saved games: what they are called, when they were last played and what they take up on disk, with
/// rename, delete and restore from a backup. The worlds list also shows the map the game built for a random gen world
/// (GeneratedWorlds), which is usually the biggest thing in there and is built again when it is needed.
/// </summary>
sealed class SavesForm : Form
{
    readonly GameInstall _install;
    readonly ListView _worlds = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Worlds" };
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, AccessibleName = "Saved games" };
    readonly Label _status = Muted();
    readonly Label _worldStatus = Muted();
    List<World> _all = new();

    public SavesForm(GameInstall install)
    {
        _install = install;
        Text = "Worlds and saves - Survivor's Toolkit";
        Font = Theme.BodyFont;
        Size = new Size(980, 700);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;

        foreach (var (name, w) in new[] { ("World", 220), ("Saves", 70), ("Map", 90), ("All of it", 90), ("Last played", 150) })
            _worlds.Columns.Add(name, (int)(w * Theme.TextScale));
        foreach (var (name, w) in new[] { ("World", 200), ("Save", 200), ("Last played", 160), ("Size", 90) })
            _list.Columns.Add(name, (int)(w * Theme.TextScale));
        _list.DoubleClick += (_, _) => { if (Picked() is { } s) Ui.Open(this, s.Folder); };
        _worlds.DoubleClick += (_, _) => { if (PickedWorld() is { } w && Directory.Exists(w.Folder)) Ui.Open(this, w.Folder); };
        _worlds.SelectedIndexChanged += (_, _) => ShowWorld();

        var worldsCard = Card("Worlds", new Control[]
        {
            _worldStatus,
            SmallBtn("Rename...", (_, _) => RenameWorld()),
            SmallBtn("Delete the map", (_, _) => DeleteMap()),
            SmallBtn("Delete the world...", (_, _) => DeleteWorld()),
            SmallBtn("Open folder", (_, _) => { if (PickedWorld() is { } w && Directory.Exists(w.Folder)) Ui.Open(this, w.Folder); }),
        }, _worlds, Row(Muted("A random gen world's map is built again by the game from its seed, so deleting only the map frees space without losing a save. Deleting a world backs up each of its saves first.")));
        worldsCard.Dock = DockStyle.Fill;

        var savesCard = Card("Saves", new Control[]
        {
            _status,
            SmallBtn("Back up", async (_, _) => await BackUpAsync()),
            SmallBtn("Restore...", (_, _) => Restore()),
            SmallBtn("Rename...", (_, _) => RenameSave()),
            SmallBtn("Delete...", (_, _) => DeleteSave()),
            SmallBtn("Open folder", (_, _) => { if (Picked() is { } s) Ui.Open(this, s.Folder); }),
            SmallBtn("Open backups", (_, _) => { Directory.CreateDirectory(AppSettings.SaveBackupFolder); Ui.Open(this, AppSettings.SaveBackupFolder); }),
        }, _list);
        savesCard.Dock = DockStyle.Fill;

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        split.Controls.Add(worldsCard, 0, 0);
        split.Controls.Add(savesCard, 0, 1);
        var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        host.Controls.Add(split);
        Controls.Add(host);
        Theme.Apply(this);
        Shown += async (_, _) => await FillAsync();
    }

    SaveGame? Picked() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SaveGame : null;
    World? PickedWorld() => _worlds.SelectedItems.Count > 0 ? _worlds.SelectedItems[0].Tag as World : null;

    /// <summary>Lists the worlds; what each takes up on disk is added up off the window's thread, since saves can be gigabytes.</summary>
    async Task FillAsync()
    {
        _worldStatus.Text = "Reading the worlds...";
        UseWaitCursor = true;
        List<World> worlds;
        try { worlds = await Task.Run(() => Worlds.List(_install)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { worlds = new List<World>(); }
        finally { UseWaitCursor = false; }
        if (IsDisposed) return;
        var pickedWorld = PickedWorld()?.Name;
        _all = worlds;
        _worlds.BeginUpdate();
        _worlds.Items.Clear();
        foreach (var w in worlds)
        {
            var item = new ListViewItem(new[]
            {
                w.Name, w.Saves.Count.ToString(), w.GeneratedSize > 0 ? Display.Size(w.GeneratedSize) : "",
                Display.Size(w.Size), w.LastPlayed?.ToString("g") ?? "never",
            }) { Tag = w };
            if (w.Name == pickedWorld) item.Selected = true;
            _worlds.Items.Add(item);
        }
        _worlds.EndUpdate();
        _worldStatus.Text = worlds.Count == 0
            ? $"No worlds in {_install.Saves}"
            : $"{worlds.Count} world{(worlds.Count == 1 ? "" : "s")}, {Display.Size(worlds.Sum(w => w.Size))} in all";

        ShowWorld();
    }

    /// <summary>The saves of the picked world, or every save when no world is picked.</summary>
    void ShowWorld()
    {
        var world = PickedWorld();
        var saves = (world != null ? world.Saves : _all.SelectMany(w => w.Saves)).OrderByDescending(s => s.LastPlayed).ToList();
        var picked = Picked()?.Folder;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var s in saves)
        {
            var item = new ListViewItem(new[] { s.World, s.Name, s.LastPlayed.ToString("g"), Display.Size(s.Size) }) { Tag = s };
            if (s.Folder == picked) item.Selected = true;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        _status.Text = saves.Count == 0
            ? world != null ? $"No saves in \"{world.Name}\"" : "No saves yet"
            : $"{saves.Count} save{(saves.Count == 1 ? "" : "s")}" + (world != null ? $" in \"{world.Name}\"" : "");
    }

    /// <summary>Nothing is renamed or deleted while the game has the files open.</summary>
    bool GameIsClosed()
    {
        if (!GameRunner.IsRunning()) return true;
        Error(this, "7 Days to Die is running. Close it first: it has these files open.");
        return false;
    }

    async Task BackUpAsync()
    {
        if (Picked() is not { } s) { Error(this, "Pick a save first."); return; }
        try
        {
            UseWaitCursor = true;
            var zip = await Task.Run(() => Saves.Backup(s, AppSettings.SaveBackupFolder));
            _status.Text = "Backed up to " + zip;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error(this, "Couldn't back up the save: " + e.Message); }
        finally { UseWaitCursor = false; }
    }

    void RenameSave()
    {
        if (Picked() is not { } s) { Error(this, "Pick a save first."); return; }
        if (!GameIsClosed()) return;
        if (Ask(this, "Rename a save", $"What should \"{s.Name}\" in {s.World} be called?", s.Name) is not { } name) return;
        Do(_status, () =>
        {
            Worlds.RenameSave(s, name);
            return $"Renamed to \"{name}\".";
        });
    }

    void RenameWorld()
    {
        if (PickedWorld() is not { } w) { Error(this, "Pick a world first."); return; }
        if (!GameIsClosed()) return;
        if (Ask(this, "Rename a world", $"What should \"{w.Name}\" be called? Its saves and its map are renamed with it.", w.Name) is not { } name) return;
        Do(_worldStatus, () =>
        {
            Worlds.RenameWorld(w, name);
            return $"Renamed to \"{name}\".";
        });
    }

    void DeleteSave()
    {
        if (Picked() is not { } s) { Error(this, "Pick a save first."); return; }
        if (!GameIsClosed()) return;
        if (!Confirm(this, $"Delete the save \"{s.Name}\" in {s.World} ({Display.Size(s.Size)})?\n\nIt is backed up to a zip first, so it can be restored.", MessageBoxIcon.Warning)) return;
        Do(_status, () =>
        {
            var zip = Worlds.DeleteSave(s, AppSettings.SaveBackupFolder);
            return zip != null ? "Deleted. The backup is " + zip : "Deleted.";
        });
    }

    void DeleteWorld()
    {
        if (PickedWorld() is not { } w) { Error(this, "Pick a world first."); return; }
        if (!GameIsClosed()) return;
        var map = w.GeneratedSize > 0 ? $"\nIts map takes {Display.Size(w.GeneratedSize)} and goes too." : "";
        if (!Confirm(this, $"Delete the world \"{w.Name}\" ({Display.Size(w.Size)})?\n\n{w.Saves.Count} save{(w.Saves.Count == 1 ? " is" : "s are")} backed up to zips first.{map}", MessageBoxIcon.Warning)) return;
        Do(_worldStatus, () =>
        {
            var zips = Worlds.DeleteWorld(w, AppSettings.SaveBackupFolder, withMap: true);
            return $"Deleted \"{w.Name}\"" + (zips.Count > 0 ? $", after backing up {zips.Count} save{(zips.Count == 1 ? "" : "s")}." : ".");
        });
    }

    void DeleteMap()
    {
        if (PickedWorld() is not { } w) { Error(this, "Pick a world first."); return; }
        if (w.Generated == null) { Error(this, $"\"{w.Name}\" has no map in {Worlds.GeneratedFolderName}: it is one of the game's own worlds, or it hasn't been generated yet."); return; }
        if (!GameIsClosed()) return;
        if (!Confirm(this, $"Delete the map of \"{w.Name}\" ({Display.Size(w.GeneratedSize)})?\n\nThe saves stay. The game builds the map again from the world's seed the next time it is needed, which takes a few minutes.", MessageBoxIcon.Warning)) return;
        Do(_worldStatus, () =>
        {
            Worlds.DeleteMap(w);
            return $"Deleted the map of \"{w.Name}\". The game builds it again when it needs it.";
        });
    }

    void Restore()
    {
        var world = PickedWorld() is { } w ? w.Folder : Picked() is { } s ? Path.GetDirectoryName(s.Folder) : null;
        if (world == null) { Error(this, "Pick the world to restore the save into first."); return; }
        if (!GameIsClosed()) return;
        Directory.CreateDirectory(AppSettings.SaveBackupFolder);
        using var d = new OpenFileDialog { Title = "Restore a save from a backup", InitialDirectory = AppSettings.SaveBackupFolder, Filter = "Save backups (*.zip)|*.zip" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var zip = d.FileName;
        if (!Confirm(this, $"Restore {Path.GetFileName(zip)} into {Path.GetFileName(world)}?\n\nA save of the same name there is backed up first, then replaced.")) return;
        Do(_status, () => "Restored to " + Worlds.RestoreSave(zip, world, AppSettings.SaveBackupFolder));
    }

    bool _working;

    /// <summary>
    /// Does something to the files off the window's thread (backing a save up to a zip first can take a while), shows
    /// what it says on <paramref name="say"/>, says what went wrong, and reads the folders again either way.
    /// </summary>
    async void Do(Label say, Func<string> work)
    {
        if (_working) { Info(this, "Still busy with the last change; try again in a moment."); return; }
        _working = true;
        try
        {
            UseWaitCursor = true;
            say.Text = "Working...";
            var said = await Task.Run(work);
            if (!IsDisposed) say.Text = said;
        }
        catch (ArgumentException e) { if (!IsDisposed) { say.Text = ""; Error(this, e.Message); } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { if (!IsDisposed) { say.Text = ""; Error(this, e.Message); } }
        finally
        {
            _working = false;
            if (!IsDisposed)
            {
                UseWaitCursor = false;
                await FillAsync();
            }
        }
    }
}
