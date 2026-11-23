using PriceTicker.App.Diagnostics;

namespace PriceTicker.Tests.App;

public sealed class AppLogTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"priceticker-log-{Guid.NewGuid():N}");

    [Fact]
    public void Create_WritesEachEventToTheDailyFileImmediately()
    {
        using var logger = AppLog.Create(_folder);

        logger.ForContext("SourceContext", "PriceTicker.Test").Warning("Yahoo returned HTTP {StatusCode}.", 429);

        // Read while the logger is still open: an event must not wait in a buffer for a crash to lose it.
        var text = ReadLog();
        Assert.Contains("[WRN] PriceTicker.Test: Yahoo returned HTTP 429.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_DropsHttpClientRequestChatterButKeepsItsWarnings()
    {
        using var logger = AppLog.Create(_folder);
        var http = logger.ForContext("SourceContext", "System.Net.Http.HttpClient.Yahoo.LogicalHandler");

        http.Information("Start processing HTTP request");
        http.Warning("Request timed out");

        var text = ReadLog();
        Assert.DoesNotContain("Start processing", text, StringComparison.Ordinal);
        Assert.Contains("Request timed out", text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string ReadLog()
    {
        var file = Assert.Single(Directory.GetFiles(_folder, "priceticker-*.log"));
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
