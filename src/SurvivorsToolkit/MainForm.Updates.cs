using static SurvivorsToolkit.Ui;

namespace SurvivorsToolkit;

/// <summary>
/// The app's own updates, the way ModWright does them: checking, downloading in the background, and installing when
/// the window closes (or at once with Restart now). Then, once, what changed.
/// </summary>
public sealed partial class MainForm
{
    readonly LinkLabel _updateReady = new() { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Visible = false, Margin = new Padding(16, 0, 0, 0), UseMnemonic = false };
    bool _checkingUpdate, _restartAfterUpdate;
    /// <summary>A downloaded, checked update; installed when the window closes.</summary>
    PendingUpdate? _pendingUpdate;

    /// <summary>
    /// Asks GitHub for a newer version. <paramref name="quiet"/> (at start-up) says nothing when there is none or GitHub
    /// can't be reached, and skips a version the player said not to be reminded about. With automatic updates on, a
    /// start-up check downloads the release in the background and installs it when the window closes, without asking.
    /// </summary>
    async Task CheckForUpdateAsync(bool quiet)
    {
        if (_pendingUpdate != null) { if (!quiet) RestartToUpdate(); return; }
        if (_checkingUpdate) return;
        _checkingUpdate = true;
        try
        {
            var r = await UpdateCheck.FindNewerAsync(UpdateCheck.CurrentVersion);
            if (IsDisposed) return;
            if (r == null)
            {
                if (!quiet) Info(this, $"You have the newest version ({UpdateCheck.CurrentVersion}), or GitHub couldn't be reached.");
                return;
            }
            if (quiet && r.Version == _s.SkippedVersion) return;

            if (quiet && _s.AutoUpdate && AppUpdates.CannotUpdateReason(r) == null)
            {
                try { SetPendingUpdate(await AppUpdates.DownloadAsync(r, null, CancellationToken.None)); }
                catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or TaskCanceledException)
                {
                    ShowNote($"Survivor's Toolkit {r.Version} is out, but it couldn't be downloaded by itself: {e.Message} Options > About > Check now tries again.", Tone.Warn);
                }
                return;
            }

            using var f = new UpdateForm(r, _dirty || _busy);
            f.ShowDialog(this);
            if (f.SkipThisVersion != (_s.SkippedVersion == r.Version))
            {
                _s.SkippedVersion = f.SkipThisVersion ? r.Version : null;
                SaveSettings();
            }
            if (f.Prepared is { } u)
            {
                SetPendingUpdate(u);
                if (f.RestartNow) RestartToUpdate();
            }
        }
        finally { _checkingUpdate = false; }
    }

    void SetPendingUpdate(PendingUpdate u)
    {
        if (IsDisposed) return;
        _pendingUpdate = u;
        _updateReady.Text = $"Version {u.Version} is downloaded and installs when you close the app.  Restart now";
        _updateReady.LinkArea = new LinkArea(_updateReady.Text.Length - "Restart now".Length, "Restart now".Length);
        _updateReady.Visible = true;
    }

    /// <summary>Closes the window (asking about unsaved ticks as usual) and starts the new version once it has.</summary>
    void RestartToUpdate()
    {
        if (_pendingUpdate == null) return;
        if (_busy) { Info(this, "The mods are being changed right now. The update installs when you close the app, or press Restart now again in a moment."); return; }
        _restartAfterUpdate = true;
        Close();
    }

    /// <summary>
    /// Hands the downloaded update to the program that installs it over this one once this process has exited (and
    /// starts it when <paramref name="restart"/>). Says why when it can't; nothing is changed then.
    /// </summary>
    void InstallPendingUpdate(bool restart)
    {
        if (_pendingUpdate is not { } u) return;
        _pendingUpdate = null;
        try { u.Install(UpdateCheck.CurrentVersion, restart); }
        // Velopack throws its own exception types too; the window is closing, so any failure is only reported.
        catch (Exception e)
        {
            Error(null, $"Couldn't install Survivor's Toolkit {u.Version}: {e.Message}\n\nNothing was changed. It is offered again the next time the app starts.");
        }
    }

    /// <summary>Once per update (however it was installed), the changelog of the versions that came with it.</summary>
    /// <param name="updatedFrom">The version the update replaced, when it restarted into this one. 0.1.0 didn't record
    /// the version that last ran, so after an update from it this is the only way to know.</param>
    void ShowWhatsNewAfterUpdate(string? updatedFrom)
    {
        var current = UpdateCheck.CurrentVersion;
        var last = _s.LastRunVersion.Length > 0 ? _s.LastRunVersion : updatedFrom ?? "";
        if (last == current) return;
        _s.LastRunVersion = current;
        SaveSettings();
        if (!WhatsNew.ShouldShow(last, current)) return;
        var sections = WhatsNew.Between(WhatsNew.BuiltIn(), last, current);
        if (sections.Count == 0) return;
        using var f = new WhatsNewForm($"Updated from {last} to {current}. Here is what changed:", sections);
        f.ShowDialog(this);
    }

    void ShowAllChanges()
    {
        var all = WhatsNew.Between(WhatsNew.BuiltIn(), "0", UpdateCheck.CurrentVersion);
        if (all.Count == 0) { Info(this, "This copy has no change notes built in."); return; }
        using var f = new WhatsNewForm($"Every version up to {UpdateCheck.CurrentVersion}, newest first:", all);
        f.ShowDialog(this);
    }
}
