using System.Diagnostics;
using System.Text;
using Cotabby.Core.Vocabulary;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Inference.Tests;

/// <summary>
/// Actual short-lived PowerShell fixture processes exercise pipe framing and process ownership.
/// Scripts contain only test bytes/a private PID marker; no clipboard APIs or model/network IO.
/// This tests cancellation of our process, not a real foreign application's delayed renderer.
/// </summary>
public sealed class VocabularyClipboardTransferTests
{
    [Theory]
    [InlineData("data")]
    [InlineData("empty")]
    [InlineData("unavailable")]
    public async Task ReturnsOnlyOneCompleteBoundedSuccessfulReply(string mode)
    {
        byte[]? expected = mode == "data" ? [5, 6, 7] : mode == "empty" ? [] : null;
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(Reply(VocabularyClipboardReaderTests.Frame(expected?.Length ?? -1, expected ?? []))), TimeSpan.FromSeconds(15));
        Assert.Equal(expected, await transfer.ReadAsync(42));
    }
    [Theory]
    [InlineData("failure")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    [InlineData("nonzero-exit")]
    [InlineData("stderr-budget")]
    public async Task MalformedFaultedOrUnboundedRepliesUseTheFixedContentFailure(string mode)
    {
        byte[] bytes = VocabularyClipboardReaderTests.Frame(1, [4]);
        string script = mode switch
        {
            "failure" => Reply(VocabularyClipboardReaderTests.Frame(-2, [])),
            "truncated" => Reply(bytes[..8]),
            "trailing" => Reply([.. bytes, 5]),
            "nonzero-exit" => Reply(bytes) + "; exit 3",
            _ => "[Console]::Error.Write([string]::new([char]120, 40000)); [Threading.Thread]::Sleep(-1)"
        };
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(script), TimeSpan.FromSeconds(30));
        // A stalled reply must fail from the stderr budget, not satisfy this assertion by timeout.
        var failure = await Assert.ThrowsAsync<VocabularyException>(() => transfer.ReadAsync(42).WaitAsync(TimeSpan.FromSeconds(12))); Assert.Equal(VocabularyFailure.InvalidContent, failure.Failure);
    }
    [Fact]
    public async Task CancellationKillsTheExactBlockedChildAndReleasesSingleFlightForNextRead()
    {
        using var fixture = new BlockedChild(); using var cancel = new CancellationTokenSource(); int starts = 0;
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Interlocked.Increment(ref starts) == 1 ? Start(fixture.Script) : Start(Reply(VocabularyClipboardReaderTests.Frame(2, [8, 9]))), TimeSpan.FromSeconds(30));
        var first = transfer.ReadAsync(42, cancel.Token);
        using var child = await fixture.WaitForProcess(first);
        try
        {
            var declined = Enumerable.Range(0, 500).Select(_ => transfer.ReadAsync(42)).ToArray();
            Assert.All(declined, task => Assert.True(task.IsCompletedSuccessfully));
            Assert.All(await Task.WhenAll(declined), value => Assert.Null(value)); Assert.Equal(1, starts);
            cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first); Assert.True(child.HasExited);
            Assert.Equal(new byte[] { 8, 9 }, await transfer.ReadAsync(42)); Assert.Equal(2, starts);
        }
        finally { cancel.Cancel(); await Ignore(first); KillIfRunning(child); }
    }
    [Fact]
    public async Task DisposalCancelsBlockedWorkWaitsForChildExitAndClosesAdmission()
    {
        using var fixture = new BlockedChild(); await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(fixture.Script), TimeSpan.FromSeconds(30));
        var read = transfer.ReadAsync(42); using var child = await fixture.WaitForProcess(read);
        try
        {
            await transfer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read); Assert.True(child.HasExited);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => transfer.ReadAsync(42)); await transfer.DisposeAsync();
        }
        finally { await transfer.DisposeAsync(); KillIfRunning(child); }
    }
    [Fact]
    public async Task TimeoutKillsAChildThatNeverFinishesItsReply()
    {
        using var fixture = new BlockedChild(); await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(fixture.Script), TimeSpan.FromSeconds(8));
        var read = transfer.ReadAsync(42); using var child = await fixture.WaitForProcess(read);
        try
        {
            var failure = await Assert.ThrowsAsync<VocabularyException>(() => read.WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.Equal(VocabularyFailure.InvalidContent, failure.Failure); Assert.True(child.HasExited);
        }
        finally { await transfer.DisposeAsync(); KillIfRunning(child); }
    }
    [Fact]
    public async Task AlreadyCancelledAdmissionNeverStartsAProcess()
    {
        int starts = 0; using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => { ++starts; return Start("exit 0"); }, TimeSpan.FromSeconds(15));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transfer.ReadAsync(42, cancel.Token)); Assert.Equal(0, starts);
    }
    private static ProcessStartInfo Start(string script)
    {
        // Use the installed OS shell by its absolute path; only this test fixture supplies code.
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"));
        start.ArgumentList.Add("-NoLogo"); start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand"); start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script))); return start;
    }
    private static string Reply(byte[] bytes) => "$payload=[Convert]::FromBase64String('" + Convert.ToBase64String(bytes) + "'); [Console]::OpenStandardOutput().Write($payload, 0, $payload.Length)";
    private static async Task Ignore(Task task) { try { await task.WaitAsync(TimeSpan.FromSeconds(10)); } catch { } }
    private static void KillIfRunning(Process child) { if (!child.HasExited) { child.Kill(); Assert.True(child.WaitForExit(5000)); } }
    private sealed class BlockedChild : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "lingrove-transfer-" + Guid.NewGuid().ToString("N"));
        private string Marker => Path.Combine(_directory, "child.pid");
        internal string Script => "[IO.File]::WriteAllText('" + Marker.Replace("'", "''") + "', $PID.ToString()); [Threading.Thread]::Sleep(-1)";
        internal BlockedChild() => Directory.CreateDirectory(_directory);
        internal async Task<Process> WaitForProcess(Task reading)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(6))
            {
                if (File.Exists(Marker) && int.TryParse(await File.ReadAllTextAsync(Marker), out int pid))
                { var process = Process.GetProcessById(pid); _ = process.Handle; return process; }
                if (reading.IsCompleted) { await reading; throw new InvalidOperationException("Fixture exited before its PID marker."); }
                await Task.Delay(25);
            }
            throw new TimeoutException("The test child did not publish its PID.");
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
