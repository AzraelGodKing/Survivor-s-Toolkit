namespace SurvivorsToolkit.Tests;

/// <summary>A console that answers from a script, so the status can be tested without a server.</summary>
sealed class FakeConsole : IServerConsole
{
    readonly Dictionary<string, string> _answers;
    public List<string> Sent { get; } = new();

    public FakeConsole(Dictionary<string, string> answers) => _answers = answers;

    public string Banner => "*** Connected with 7DTD server.";

    public Task<string> SendAsync(string command, CancellationToken ct)
    {
        Sent.Add(command);
        return Task.FromResult(_answers.TryGetValue(command, out var a) ? a : "");
    }

    public void Dispose() { }
}

public class ServerStatusTests
{
    const string Lp = """
        1. id=171, Azrael, pos=(1.2, 34.5, -6.7), rot=(0.0, 90.0, 0.0), remote=True, health=93, deaths=2, zombies=48, players=0, score=12, level=27, pseudoRemote=False, steamid=Steam_76561198000000000, ip=203.0.113.4, ping=38
        2. id=172, Wolfie Two, pos=(5.0, 40.0, 9.0), rot=(0.0, 0.0, 0.0), remote=True, health=100, deaths=0, zombies=3, players=0, score=1, level=4, pseudoRemote=False, steamid=Steam_76561198000000001, ip=203.0.113.5, ping=112
        Total of 2 in the game
        """;

    [Fact]
    public void ReadsTheClock()
    {
        Assert.Equal((12, 8, 34), ServerStatusReader.ParseTime("Day 12, 08:34"));
        Assert.Equal((null, null, null), ServerStatusReader.ParseTime("Press any key"));
    }

    [Fact]
    public void ReadsThePlayers()
    {
        var players = ServerStatusReader.ParsePlayers(Lp);
        Assert.Equal(2, players.Count);
        Assert.Equal(new[] { "Azrael", "Wolfie Two" }, players.Select(p => p.Name));
        Assert.Equal(171, players[0].Id);
        Assert.Equal(93, players[0].Health);
        Assert.Equal(27, players[0].Level);
        Assert.Equal(38, players[0].Ping);
        Assert.Equal(2, players[0].Deaths);
        Assert.Equal("Steam_76561198000000000", players[0].SteamId);
        Assert.Equal(2, ServerStatusReader.ParseTotal(Lp));
    }

    [Fact]
    public void AnEmptyServerHasNoPlayers()
    {
        Assert.Empty(ServerStatusReader.ParsePlayers("Total of 0 in the game"));
        Assert.Equal(0, ServerStatusReader.ParseTotal("Total of 0 in the game"));
    }

    [Fact]
    public void ReadsTheGameVersion() =>
        Assert.Equal("V 1.0 (b333)", ServerStatusReader.ParseVersion("Game version: V 1.0 (b333) Compatibility Version: V 1.0"));

    [Theory]
    [InlineData(1, 7, 7)]
    [InlineData(6, 7, 7)]
    [InlineData(7, 7, 7)]   // on the day itself the horde is still to come, at 22:00
    [InlineData(8, 7, 14)]
    [InlineData(21, 7, 21)]
    [InlineData(3, 1, 3)]
    public void CountsToTheNextBloodMoon(int day, int frequency, int expected) =>
        Assert.Equal(expected, ServerStatusReader.NextBloodMoonDay(day, frequency));

    [Fact]
    public void NoCountdownWithoutADayOrAFrequency()
    {
        Assert.Null(ServerStatusReader.NextBloodMoonDay(null, 7));
        Assert.Null(ServerStatusReader.NextBloodMoonDay(5, 0));
    }

    [Fact]
    public void SaysWhenTheBloodMoonComes()
    {
        Assert.Equal("tonight (day 7, 22:00)", Status(7, 20).BloodMoon);
        Assert.Equal("now (day 7)", Status(7, 22).BloodMoon);
        Assert.Equal("tomorrow night (day 14)", Status(13, 9).BloodMoon);
        Assert.Equal("in 4 days (day 14)", Status(10, 9).BloodMoon);
        static ServerStatus Status(int day, int hour) =>
            new(day, hour, 0, 0, null, ServerStatusReader.NextBloodMoonDay(day, 7), new List<PlayerOnServer>());
    }

    [Fact]
    public async Task AsksTheConsoleForEverythingTheStatusShows()
    {
        var console = new FakeConsole(new Dictionary<string, string>
        {
            ["gettime"] = "Day 12, 08:34",
            ["lp"] = Lp,
            ["version"] = "Game version: V 1.0 (b333) Compatibility Version: V 1.0",
        });
        var status = await ServerStatusReader.QueryAsync(console, 7);
        Assert.Equal(ServerStatusReader.StatusCommands, console.Sent);
        Assert.Equal("Day 12, 08:34", status.Clock);
        Assert.Equal(2, status.Players);
        Assert.Equal(14, status.BloodMoonDay);
        Assert.Equal("in 2 days (day 14)", status.BloodMoon);
        Assert.Equal("V 1.0 (b333)", status.Version);
    }

    [Fact]
    public async Task AServerThatSaysNothingLeavesTheStatusEmpty()
    {
        var status = await ServerStatusReader.QueryAsync(new FakeConsole(new Dictionary<string, string>()), 7);
        Assert.Equal("?", status.Clock);
        Assert.Equal("?", status.BloodMoon);
        Assert.Equal(0, status.Players);
        Assert.Null(status.Version);
    }
}

public class ServerCommandTests
{
    [Fact]
    public void QuotesWhatIsSaid()
    {
        Assert.Equal("say \"Restart in 5 minutes\"", ServerCommands.Say("Restart in 5 minutes"));
        Assert.Equal("say \"he said 'hi'\"", ServerCommands.Say("he said \"hi\""));
    }

    [Fact]
    public void KicksByIdOrName()
    {
        Assert.Equal("kick 171 \"afk\"", ServerCommands.Kick("171", "afk"));
        Assert.Equal("kick \"Wolfie Two\"", ServerCommands.Kick("Wolfie Two", null));
        Assert.Equal("ban add Steam_765 1 day \"griefing\"", ServerCommands.Ban("Steam_765", "1 day", "griefing"));
    }

    [Theory]
    [InlineData("shutdown", true)]
    [InlineData("  KICK 171", true)]
    [InlineData("ban add 171 1 day", true)]
    [InlineData("saveworld", false)]
    [InlineData("gettime", false)]
    [InlineData("say \"hello\"", false)]
    public void AsksFirstAboutWhatCantBeTakenBack(string command, bool asks) =>
        Assert.Equal(asks, ServerCommands.NeedsConfirming(command));
}

public class TelnetTests
{
    [Fact]
    public void DropsTelnetsOwnNegotiationBytes()
    {
        var bytes = new List<byte> { 255, 251, 1, (byte)'h', (byte)'i', 255, 241, (byte)'!' };
        Assert.Equal("hi!", System.Text.Encoding.UTF8.GetString(TelnetConsole.Strip(bytes).ToArray()));
    }

    [Fact]
    public void TidiesAnAnswerForShowing() =>
        Assert.Equal("Day 12, 08:34", TelnetConsole.Clean("Day 12, 08:34\r\n\r\n"));

    [Fact]
    public void ATargetNeedsAnAddressAndAPort()
    {
        Assert.False(new TelnetTarget { Host = "" }.IsSet);
        Assert.True(new TelnetTarget().IsSet);
        Assert.Equal("127.0.0.1:8081", new TelnetTarget().Describe);
    }
}
