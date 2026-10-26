namespace SurvivorsToolkit.Tests;

public class RestartTests
{
    static readonly DateTime Noon = new(2026, 10, 26, 12, 0, 0);

    [Theory]
    [InlineData("05:00", 5, 0)]
    [InlineData("5:00", 5, 0)]
    [InlineData(" 23.45 ", 23, 45)]
    public void ReadsATimeOfDay(string text, int hour, int minute) =>
        Assert.Equal(new TimeSpan(hour, minute, 0), Restarts.ReadTime(text));

    [Theory]
    [InlineData("")]
    [InlineData("tea time")]
    [InlineData("25:00")]
    [InlineData("5")]
    public void SaysWhenATimeCantBeRead(string text) => Assert.Null(Restarts.ReadTime(text));

    [Fact]
    public void FindsTheNextRestartTodayOrTomorrow()
    {
        var plan = new RestartPlan { Times = new List<string> { "17:00", "05:00" } };
        Assert.Equal(new DateTime(2026, 10, 26, 17, 0, 0), plan.NextRun(Noon));
        Assert.Equal(new DateTime(2026, 10, 27, 5, 0, 0), plan.NextRun(new DateTime(2026, 10, 26, 18, 0, 0)));
    }

    [Fact]
    public void NoTimesMeansNoRestart() =>
        Assert.Null(new RestartPlan { Times = new List<string> { "half past nine" } }.NextRun(Noon));

    [Fact]
    public void WarnsThenSavesThenShutsDown()
    {
        var steps = new RestartPlan { WarnMinutes = new List<int> { 5, 15, 1, 0 } }.Timeline(Noon);
        Assert.Equal(new[] { RestartStep.Warn, RestartStep.Warn, RestartStep.Warn, RestartStep.SaveWorld, RestartStep.Shutdown }, steps.Select(s => s.Step));
        Assert.Equal(Noon.AddMinutes(-15), steps[0].When);
        Assert.Equal(Noon.AddMinutes(-5), steps[1].When);
        Assert.Equal(Noon.AddMinutes(-1), steps[2].When);
        Assert.Equal("say \"The server restarts in 15 minutes. Get somewhere safe.\"", steps[0].Command);
        Assert.Equal("say \"The server restarts in 1 minute. Get somewhere safe.\"", steps[2].Command);
        Assert.Equal("saveworld", steps[3].Command);
        Assert.Equal("shutdown", steps[4].Command);
    }

    [Fact]
    public void CanSkipTheSave() =>
        Assert.DoesNotContain(RestartStep.SaveWorld, new RestartPlan { SaveWorld = false }.Timeline(Noon).Select(s => s.Step));

    [Fact]
    public void StartsAServerOnThisPcAgainAndSaysItIsBack()
    {
        var steps = new RestartPlan { WarnMinutes = new List<int>(), StartAgain = true, StartAgainAfterSeconds = 30, BackAfterSeconds = 120 }.Timeline(Noon);
        Assert.Equal(new[] { RestartStep.SaveWorld, RestartStep.Shutdown, RestartStep.StartAgain, RestartStep.Back }, steps.Select(s => s.Step));
        Assert.Equal(Noon.AddSeconds(30), steps[2].When);
        Assert.Null(steps[2].Command);
        Assert.Equal(Noon.AddSeconds(150), steps[3].When);
        Assert.StartsWith("say \"The server is back up.", steps[3].Command);
    }

    [Fact]
    public void NothingIsSaidWhenThereIsNoBackMessage() =>
        Assert.DoesNotContain(RestartStep.Back,
            new RestartPlan { StartAgain = true, BackAfterSeconds = 0 }.Timeline(Noon).Select(s => s.Step));

    [Fact]
    public void TheWarningTakesTheRestartsTime() =>
        Assert.Equal("Restart at 12:00 - 10 minutes left", Restarts.Warning("Restart at {time} - {minutes} minutes left", 10, Noon));

    [Fact]
    public void AnEmptyWarningFallsBackToTheDefaultOne() =>
        Assert.Contains("5 minutes", Restarts.Warning("  ", 5, Noon));

    [Fact]
    public async Task WaitsForEachStepThenDoesIt()
    {
        var plan = new RestartPlan { WarnMinutes = new List<int> { 10, 1 } };
        var clock = Noon;
        var done = new List<string>();
        var waited = new List<TimeSpan>();
        await Restarts.RunAsync(plan.Timeline(Noon.AddMinutes(10)),
            (step, _) => { done.Add(step.Step.ToString()); return Task.CompletedTask; },
            () => clock,
            (wait, _) => { waited.Add(wait); clock += wait; return Task.CompletedTask; },
            CancellationToken.None);
        Assert.Equal(new[] { "Warn", "Warn", "SaveWorld", "Shutdown" }, done);
        // The first warning is due right now, so only the second one and the restart itself are waited for.
        Assert.Equal(new[] { TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(1) }, waited);
    }

    [Fact]
    public async Task StoppingItLeavesTheRestOfTheRestartUndone()
    {
        var cts = new CancellationTokenSource();
        var done = new List<RestartStep>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Restarts.RunAsync(
            new RestartPlan { WarnMinutes = new List<int> { 5 } }.Timeline(Noon),
            (step, _) => { done.Add(step.Step); cts.Cancel(); return Task.CompletedTask; },
            () => Noon, (_, _) => Task.CompletedTask, cts.Token));
        Assert.Equal(new[] { RestartStep.Warn }, done);
    }
}

public class HowLongTests
{
    [Theory]
    [InlineData(0, "now")]
    [InlineData(30, "in less than a minute")]
    [InlineData(60, "in 1 minute")]
    [InlineData(600, "in 10 minutes")]
    [InlineData(3600, "in 1 hour")]
    [InlineData(4500, "in 1 hour 15 minutes")]
    [InlineData(7200, "in 2 hours")]
    public void SaysHowLongIsLeft(int seconds, string expected) =>
        Assert.Equal(expected, Display.In(TimeSpan.FromSeconds(seconds)));
}
