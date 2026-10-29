using Cotabby.Core.CLI;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.Win32.CLI;

/// <summary>Short-lived local client. It never opens the notebook; callers select the exact app
/// identity and may supply an explicit launch action after the request is published.</summary>
public static class CliMailboxClient
{
    public static async Task<CliReply> SendAsync(string root, CliRequest request, Func<CancellationToken, Task>? launch = null,
        CancellationToken cancellationToken = default)
        => await SendCoreAsync(root, request, launch, cancellationToken, null).ConfigureAwait(false);

    internal static async Task<CliReply> SendCoreAsync(string root, CliRequest request, Func<CancellationToken, Task>? launch,
        CancellationToken cancellationToken, Action? sharingObserved)
    {
        ArgumentNullException.ThrowIfNull(request);
        var retained = CliWire.DecodeRequest(CliWire.Encode(request));
        if (retained.Version != 1 || retained.ExpiresAt <= CliWire.Now()) throw new CliException(CliErrorCode.Timeout, "Request expired.");
        using var mailbox = new CliMailboxDirectory(root);
        using var pending = new CliMailboxDirectory(System.IO.Path.Combine(root, "requests"));
        using var replies = new CliMailboxDirectory(System.IO.Path.Combine(root, "replies"));
        var name = retained.Id.ToString("D") + ".json";
        var identity = pending.PublishOwned(name, CliWire.Encode(retained));
        bool Admit(byte[] data)
        {
            var candidate = CliWire.DecodeReply(data);
            return candidate.Version == 1 && candidate.RequestId == retained.Id;
        }
        try
        {
            if (launch is not null) await launch(cancellationToken).ConfigureAwait(false);
            while (CliWire.Now() < retained.ExpiresAt)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[]? data;
                try { data = replies.Consume(name, Admit); }
                catch (VocabularyFileSharingException) { sharingObserved?.Invoke(); data = null; }
                if (data is not null)
                {
                    var reply = CliWire.DecodeReply(data);
                    if (reply.Version != 1 || reply.RequestId != retained.Id) throw new CliException(CliErrorCode.Io, "CLI response identity mismatch.");
                    return reply;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            throw new CliException(CliErrorCode.Timeout, "Timed out. A write may already be committed; retry with the same key and content.");
        }
        finally
        {
            // Claimed requests are owned by the server and are deliberately left to its drain.
            // A replacement leaf is never deleted even if its bytes happen to match ours.
            try { pending.RemoveOwned(name, identity); } catch { }
            try { _ = replies.Consume(name, Admit); } catch { }
        }
    }
}
