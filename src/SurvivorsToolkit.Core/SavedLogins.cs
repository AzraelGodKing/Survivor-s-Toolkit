namespace SurvivorsToolkit.Core;

/// <summary>
/// A server login the player chose to keep under a name ("My 7d2d.net server"), so it can be put back in Server sync
/// or the server panel with one click instead of typed again. One login can hold the FTP side (for Server sync), the
/// telnet side (for the server panel), or both, since a game host usually gives you both for the same server.
/// Passwords are only kept when the player says so, and then encrypted for their Windows account (<see cref="Secrets"/>).
/// </summary>
public sealed class SavedLogin
{
    public string Name { get; set; } = "";
    /// <summary>When it was last saved or used, so the most recent comes first.</summary>
    public DateTime LastUsed { get; set; }

    /// <summary>The FTP side, or null when this login has none.</summary>
    public ServerTarget? Ftp { get; set; }
    /// <summary>The telnet side, or null when this login has none.</summary>
    public TelnetTarget? Telnet { get; set; }

    public bool HasFtp => Ftp is { Kind: ServerKind.Ftp } f && f.IsSet;
    public bool HasTelnet => Telnet is { } t && t.IsSet;

    /// <summary>"name  (host)" for a list.</summary>
    public string Describe(bool telnet)
    {
        var where = telnet ? Telnet?.Describe : Ftp is { } f ? (f.User.Length > 0 ? f.User + "@" : "") + f.Host : null;
        return string.IsNullOrEmpty(where) || where.Equals(Name, StringComparison.OrdinalIgnoreCase) ? Name : $"{Name}  ({where})";
    }

    public override string ToString() => Name;
}

/// <summary>Keeping, finding and putting back <see cref="SavedLogin"/>s.</summary>
public static class SavedLogins
{
    /// <summary>The logins that have the side a window needs, most recently used first.</summary>
    public static List<SavedLogin> For(IEnumerable<SavedLogin> logins, bool telnet) =>
        logins.Where(l => telnet ? l.HasTelnet : l.HasFtp).OrderByDescending(l => l.LastUsed).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static SavedLogin? Find(IEnumerable<SavedLogin> logins, string name) =>
        logins.FirstOrDefault(l => l.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A name to offer when saving: the host, which is what most people know their server by.</summary>
    public static string SuggestName(string host) => host.Trim() is { Length: > 0 } h && h != "127.0.0.1" && !h.Equals("localhost", StringComparison.OrdinalIgnoreCase) ? h : "My server";

    /// <summary>
    /// Keeps the FTP side of <paramref name="target"/> under <paramref name="name"/>, adding the login or updating the
    /// one with that name (its telnet side is left as it was). The password is dropped unless <paramref name="keepPassword"/>.
    /// </summary>
    public static SavedLogin SaveFtp(List<SavedLogin> logins, string name, ServerTarget target, bool keepPassword, DateTime now)
    {
        var l = GetOrAdd(logins, name, now);
        l.Ftp = new ServerTarget
        {
            Kind = ServerKind.Ftp, Host = target.Host.Trim(), Port = target.Port, User = target.User.Trim(),
            ProtectedPassword = keepPassword ? target.ProtectedPassword : null, RemotePath = target.RemotePath, Tls = target.Tls,
        };
        return l;
    }

    /// <summary>Keeps the telnet side of <paramref name="target"/> under <paramref name="name"/>, as <see cref="SaveFtp"/> does for FTP.</summary>
    public static SavedLogin SaveTelnet(List<SavedLogin> logins, string name, TelnetTarget target, bool keepPassword, DateTime now)
    {
        var l = GetOrAdd(logins, name, now);
        l.Telnet = new TelnetTarget
        {
            Host = target.Host.Trim(), Port = target.Port, BloodMoonFrequency = target.BloodMoonFrequency,
            ProtectedPassword = keepPassword ? target.ProtectedPassword : null,
        };
        return l;
    }

    /// <summary>Puts a login's FTP side into <paramref name="target"/>. False when it has none.</summary>
    public static bool UseFtp(SavedLogin login, ServerTarget target, DateTime now)
    {
        if (!login.HasFtp) return false;
        var f = login.Ftp!;
        target.Kind = ServerKind.Ftp;
        target.Host = f.Host;
        target.Port = f.Port;
        target.User = f.User;
        target.ProtectedPassword = f.ProtectedPassword;
        target.RemotePath = f.RemotePath;
        target.Tls = f.Tls;
        login.LastUsed = now;
        return true;
    }

    /// <summary>Puts a login's telnet side into <paramref name="target"/>. False when it has none.</summary>
    public static bool UseTelnet(SavedLogin login, TelnetTarget target, DateTime now)
    {
        if (!login.HasTelnet) return false;
        var t = login.Telnet!;
        target.Host = t.Host;
        target.Port = t.Port;
        target.ProtectedPassword = t.ProtectedPassword;
        target.BloodMoonFrequency = t.BloodMoonFrequency;
        login.LastUsed = now;
        return true;
    }

    /// <summary>
    /// Forgets one side of a login (the side the window is about); the whole login goes once it has neither side.
    /// True when the login was removed.
    /// </summary>
    public static bool Forget(List<SavedLogin> logins, SavedLogin login, bool telnet)
    {
        if (telnet) login.Telnet = null; else login.Ftp = null;
        if (login.HasFtp || login.HasTelnet) return false;
        logins.Remove(login);
        return true;
    }

    static SavedLogin GetOrAdd(List<SavedLogin> logins, string name, DateTime now)
    {
        var l = Find(logins, name);
        if (l == null) logins.Add(l = new SavedLogin { Name = name.Trim() });
        l.LastUsed = now;
        return l;
    }
}
