using PriceTicker.App.Platform;

namespace PriceTicker.Tests.App;

public sealed class SingleInstanceTests
{
    private readonly string _name = $"PriceTicker-test-{Guid.NewGuid():N}";

    [Fact]
    public void SecondLaunch_AsksTheFirstToShowItself_OnWindows()
    {
        using var first = SingleInstance.Acquire(_name);
        using var shown = new ManualResetEventSlim();
        first.ShowRequested += (_, _) => shown.Set();
        first.Listen();

        // Another thread stands in for another process: a named mutex is owned per thread.
        var secondIsFirst = RunOnOtherThread(() =>
        {
            using var second = SingleInstance.Acquire(_name);
            return second.IsFirst;
        });

        Assert.True(first.IsFirst);
        if (OperatingSystem.IsWindows())
        {
            Assert.False(secondIsFirst);
            Assert.True(shown.Wait(TimeSpan.FromSeconds(5)), "The first copy was not asked to show itself.");
        }
        else
        {
            // Elsewhere every launch runs on its own.
            Assert.True(secondIsFirst);
        }
    }

    [Fact]
    public void AfterTheFirstCopyExits_TheNextLaunchRunsNormally()
    {
        RunOnOtherThread(() =>
        {
            using var first = SingleInstance.Acquire(_name);
            return first.IsFirst;
        });

        using var next = SingleInstance.Acquire(_name);

        Assert.True(next.IsFirst);
    }

    private static bool RunOnOtherThread(Func<bool> action)
    {
        var result = false;
        var thread = new Thread(() => result = action());
        thread.Start();
        thread.Join();
        return result;
    }
}
