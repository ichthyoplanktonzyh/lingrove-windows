using Cotabby.App.Hosting;
using Microsoft.Extensions.Logging;

namespace Cotabby.App.Tests;

public sealed class FileLoggerLifetimeTests
{
    [Fact]
    public void FailedWriterDisposalStillWithdrawsEveryLogger()
    {
        using var provider = new FileLoggerProvider(new ThrowingWriter());
        var logger = provider.CreateLogger("lifetime");
        Assert.Throws<IOException>(provider.Dispose);
        Assert.False(logger.IsEnabled(LogLevel.Error));
        logger.LogError("Late synthetic shutdown failure.");
        provider.Dispose();
    }

    private sealed class ThrowingWriter() : StreamWriter(new MemoryStream())
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) throw new IOException("Synthetic flush failure.");
        }
    }

    [Fact]
    public void RestartFailureCanBeReportedAfterProviderRetirement()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lingrove-log-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var provider = new FileLoggerProvider(Path.Combine(directory, "test.log"));
            var logger = provider.CreateLogger("lifetime");
            provider.Dispose();
            Assert.False(logger.IsEnabled(LogLevel.Error));
            logger.LogError("Late synthetic restart failure.");
            Assert.Empty(File.ReadAllText(Path.Combine(directory, "test.log")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task DisposalBetweenFormattingAndWritingClosesAdmission()
    {
        string directory = Path.Combine(Path.GetTempPath(), "lingrove-log-test-" + Guid.NewGuid().ToString("N"));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            using var provider = new FileLoggerProvider(Path.Combine(directory, "test.log"));
            var logger = provider.CreateLogger("lifetime");
            var writing = Task.Run(() => logger.Log(LogLevel.Error, default, "owned synthetic event", null,
                (state, _) => { entered.Set(); release.Wait(); return state; }));
            try
            {
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                provider.Dispose();
            }
            finally { release.Set(); }
            await writing.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(File.ReadAllText(Path.Combine(directory, "test.log")));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
