using Microsoft.Extensions.Logging.Abstractions;
using PriceTicker.App.Platform;
using PriceTicker.Tests.TestDoubles;

namespace PriceTicker.Tests.App;

public sealed class RunKeyAutoStartTests
{
    private const string ExePath = @"D:\01. Work\PriceTicker\publish\PriceTicker.exe";
    private const string QuotedExePath = "\"" + ExePath + "\"";

    private readonly FakeRunKey _runKey = new();

    [Fact]
    public void TrySetEnabled_True_WritesQuotedExecutablePath()
    {
        var autoStart = CreateAutoStart();

        Assert.True(autoStart.TrySetEnabled(true));

        Assert.Equal(QuotedExePath, _runKey.Values[RunKeyAutoStart.ValueName]);
        Assert.True(autoStart.IsEnabled);
    }

    [Fact]
    public void TrySetEnabled_False_RemovesEntry()
    {
        _runKey.Values[RunKeyAutoStart.ValueName] = QuotedExePath;
        var autoStart = CreateAutoStart();

        Assert.True(autoStart.TrySetEnabled(false));

        Assert.Empty(_runKey.Values);
        Assert.False(autoStart.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WhenEntryPointsToAnotherCopy_IsFalse()
    {
        _runKey.Values[RunKeyAutoStart.ValueName] = "\"C:\\Old\\PriceTicker.exe\"";

        Assert.False(CreateAutoStart().IsEnabled);
    }

    [Fact]
    public void IsEnabled_IgnoresPathCase()
    {
        _runKey.Values[RunKeyAutoStart.ValueName] = QuotedExePath.ToUpperInvariant();

        Assert.True(CreateAutoStart().IsEnabled);
    }

    [Fact]
    public void TrySetEnabled_WhenRegistryRefuses_ReturnsFalse()
    {
        _runKey.FailWith = new UnauthorizedAccessException();
        var autoStart = CreateAutoStart();

        Assert.False(autoStart.TrySetEnabled(true));
        Assert.False(autoStart.IsEnabled);
    }

    [Fact]
    public void TrySetEnabled_False_RemovesEntryOfAnotherCopy()
    {
        _runKey.Values[RunKeyAutoStart.ValueName] = "\"C:\\Old\\PriceTicker.exe\"";

        Assert.True(CreateAutoStart().TrySetEnabled(false));

        Assert.Empty(_runKey.Values);
    }

    [Fact]
    public void TrySetEnabled_False_WhenRegistryRefuses_ReturnsFalse()
    {
        _runKey.Values[RunKeyAutoStart.ValueName] = QuotedExePath;
        var autoStart = CreateAutoStart();
        _runKey.FailWith = new UnauthorizedAccessException();

        Assert.False(autoStart.TrySetEnabled(false));
    }

    [Fact]
    public void IsEnabled_WhenRegistryCannotBeRead_IsFalseInsteadOfThrowing()
    {
        _runKey.FailWith = new System.Security.SecurityException();

        Assert.False(CreateAutoStart().IsEnabled);
    }

    private RunKeyAutoStart CreateAutoStart() =>
        new(_runKey, ExePath, NullLogger<RunKeyAutoStart>.Instance);
}
