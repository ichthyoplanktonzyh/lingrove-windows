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
        using var fixture = new BlockedChild();
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(fixture.ReplyScript(
            Reply(VocabularyClipboardReaderTests.Frame(expected?.Length ?? -1, expected ?? [])))), TimeSpan.FromSeconds(60));
        var read = transfer.ReadAsync(42);
        using var child = await fixture.WaitForProcess(read, TimeSpan.FromSeconds(30));
        try
        {
            // A successful frame is not a PowerShell startup benchmark. Observe this exact
            // child before releasing bytes; reply/EOF/exit must then complete within 12 seconds.
            // The 60-second fallback cannot turn a missing reply into a successful null result.
            fixture.ReleaseReply();
            Assert.Equal(expected, await read.WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.True(child.HasExited);
        }
        finally { await transfer.DisposeAsync(); KillIfRunning(child); }
    }
    [Theory]
    [InlineData("failure")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    [InlineData("nonzero-exit")]
    public async Task MalformedFaultedOrUnboundedRepliesUseTheFixedContentFailure(string mode)
    {
        byte[] bytes = VocabularyClipboardReaderTests.Frame(1, [4]);
        string script = mode switch
        {
            "failure" => Reply(VocabularyClipboardReaderTests.Frame(-2, [])),
            "truncated" => Reply(bytes[..8]),
            "trailing" => Reply([.. bytes, 5]),
            "nonzero-exit" => Reply(bytes) + "; exit 3",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(script), TimeSpan.FromSeconds(30));
        // Protocol rejection must finish before the operation's fallback deadline.
        var failure = await Assert.ThrowsAsync<VocabularyException>(() => transfer.ReadAsync(42).WaitAsync(TimeSpan.FromSeconds(12))); Assert.Equal(VocabularyFailure.InvalidContent, failure.Failure);
    }
    [Fact]
    public async Task StderrBudgetKillsAStartedChildBeforeTheOperationDeadline()
    {
        using var fixture = new BlockedChild();
        await using var transfer = new WindowsVocabularyClipboardTransfer(_ => Start(fixture.StderrBudgetScript), TimeSpan.FromSeconds(60));
        var read = transfer.ReadAsync(42);
        using var child = await fixture.WaitForProcess(read, TimeSpan.FromSeconds(15));
        try
        {
            // PowerShell startup is a separate observation: the child publishes its PID before
            // waiting for release. Only then does the 12-second pipe-drain assertion begin. The
            // 60-second operation deadline cannot satisfy it, even at the startup bound.
            fixture.ReleaseStderr();
            var failure = await Assert.ThrowsAsync<VocabularyException>(() => read.WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.Equal(VocabularyFailure.InvalidContent, failure.Failure);
            Assert.True(child.HasExited);
        }
        finally { await transfer.DisposeAsync(); KillIfRunning(child); }
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
        private string PendingMarker => Path.Combine(_directory, "child.pid.pending");
        private string ReleaseMarker => Path.Combine(_directory, "write-stderr");
        private string ReplyMarker => Path.Combine(_directory, "write-reply");
        // Existence is the parent's readiness signal. Publish only after WriteAllText closes
        // its exclusive Windows handle, so the parent cannot read a partial/locked marker.
        private string PublishPid => "[IO.File]::WriteAllText('" + PendingMarker.Replace("'", "''") + "', $PID.ToString()); " +
            "[IO.File]::Move('" + PendingMarker.Replace("'", "''") + "', '" + Marker.Replace("'", "''") + "'); ";
        internal string Script => PublishPid + "[Threading.Thread]::Sleep(-1)";
        internal string StderrBudgetScript => PublishPid + "while (-not [IO.File]::Exists('" + ReleaseMarker.Replace("'", "''") + "')) { [Threading.Thread]::Sleep(25) }; " +
            "$payload=[byte[]]::new(40000); $pipe=[Console]::OpenStandardError(); $pipe.Write($payload,0,$payload.Length); $pipe.Flush(); [Threading.Thread]::Sleep(-1)";
        internal string ReplyScript(string reply) => PublishPid + "while (-not [IO.File]::Exists('" + ReplyMarker.Replace("'", "''") + "')) { [Threading.Thread]::Sleep(25) }; " + reply;
        internal void ReleaseReply() => File.WriteAllText(ReplyMarker, "go");
        internal void ReleaseStderr() => File.WriteAllText(ReleaseMarker, "go");
        internal BlockedChild() => Directory.CreateDirectory(_directory);
        internal async Task<Process> WaitForProcess(Task reading, TimeSpan? startupLimit = null)
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < (startupLimit ?? TimeSpan.FromSeconds(6)))
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
