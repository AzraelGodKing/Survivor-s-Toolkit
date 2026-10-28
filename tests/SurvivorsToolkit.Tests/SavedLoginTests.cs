namespace SurvivorsToolkit.Tests;

public class SavedLoginTests
{
    static readonly DateTime Then = new(2026, 10, 27, 12, 0, 0);

    static ServerTarget Ftp(string host = "ftp.7d2d.net") =>
        new() { Kind = ServerKind.Ftp, Host = host, Port = 2121, User = "azrael", ProtectedPassword = "secret", RemotePath = "7daystodie/Mods", Tls = true };

    [Fact]
    public void KeepsAndPutsBackTheFtpSide()
    {
        var logins = new List<SavedLogin>();
        SavedLogins.SaveFtp(logins, " My server ", Ftp(), keepPassword: true, Then);
        var l = Assert.Single(logins);
        Assert.Equal("My server", l.Name);
        Assert.True(l.HasFtp);
        Assert.False(l.HasTelnet);

        var target = new ServerTarget();
        Assert.True(SavedLogins.UseFtp(l, target, Then.AddDays(1)));
        Assert.Equal((ServerKind.Ftp, "ftp.7d2d.net", 2121, "azrael", "secret", "7daystodie/Mods", true),
            (target.Kind, target.Host, target.Port, target.User, target.ProtectedPassword, target.RemotePath, target.Tls));
        Assert.Equal(Then.AddDays(1), l.LastUsed);
        Assert.False(SavedLogins.UseTelnet(l, new TelnetTarget(), Then));
    }

    [Fact]
    public void ThePasswordIsOnlyKeptWhenAsked()
    {
        var logins = new List<SavedLogin>();
        var l = SavedLogins.SaveFtp(logins, "a", Ftp(), keepPassword: false, Then);
        Assert.Null(l.Ftp!.ProtectedPassword);
        var t = SavedLogins.SaveTelnet(logins, "a", new TelnetTarget { Host = "1.2.3.4", Port = 8081, ProtectedPassword = "x" }, keepPassword: false, Then);
        Assert.Null(t.Telnet!.ProtectedPassword);
    }

    [Fact]
    public void BothSidesShareALoginByName()
    {
        var logins = new List<SavedLogin>();
        SavedLogins.SaveFtp(logins, "Horde", Ftp(), true, Then);
        SavedLogins.SaveTelnet(logins, "horde", new TelnetTarget { Host = "51.0.0.1", Port = 8082, BloodMoonFrequency = 5 }, true, Then);
        var l = Assert.Single(logins);
        Assert.True(l.HasFtp && l.HasTelnet);
        Assert.Equal("Horde  (51.0.0.1:8082)", l.Describe(telnet: true));
        Assert.Equal("Horde  (azrael@ftp.7d2d.net)", l.Describe(telnet: false));

        // Forgetting one side keeps the other; forgetting both removes the login.
        Assert.False(SavedLogins.Forget(logins, l, telnet: true));
        Assert.Single(logins);
        Assert.True(SavedLogins.Forget(logins, l, telnet: false));
        Assert.Empty(logins);
    }

    [Fact]
    public void ListsOnlyTheSideAWindowNeedsNewestFirst()
    {
        var logins = new List<SavedLogin>();
        SavedLogins.SaveFtp(logins, "old", Ftp(), true, Then);
        SavedLogins.SaveFtp(logins, "new", Ftp("ftp.other"), true, Then.AddHours(1));
        SavedLogins.SaveTelnet(logins, "panel only", new TelnetTarget(), true, Then);
        Assert.Equal(new[] { "new", "old" }, SavedLogins.For(logins, telnet: false).Select(l => l.Name));
        Assert.Equal(new[] { "panel only" }, SavedLogins.For(logins, telnet: true).Select(l => l.Name));
    }

    [Fact]
    public void SuggestsTheHostAsAName()
    {
        Assert.Equal("ftp.7d2d.net", SavedLogins.SuggestName(" ftp.7d2d.net "));
        Assert.Equal("My server", SavedLogins.SuggestName("127.0.0.1"));
        Assert.Equal("My server", SavedLogins.SuggestName(""));
    }
}
