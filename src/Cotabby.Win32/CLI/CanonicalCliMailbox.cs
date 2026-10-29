using Cotabby.Core.CLI;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.Win32.CLI;

/// <summary>Creates an inert mailbox server borrowing the process-owned canonical Store.
/// Composition starts it once and drains it before disposing that Store.</summary>
public static class CanonicalCliMailbox
{
    public static CliMailboxServer Create(string root, ICanonicalVocabularyCliStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new CliMailboxServer(root, async request =>
        {
            try { return new CliReply { RequestId = request.Id, Ok = true, Result = await store.ExecuteCliAsync(request).ConfigureAwait(false) }; }
            catch (CliException error) { return CliWire.Failure(request.Id, error); }
            catch (VocabularyException error)
            {
                var mapped = error.Failure switch
                {
                    VocabularyFailure.EditConflict => new CliException(CliErrorCode.Conflict, "Notebook changed. Read it again before writing."),
                    VocabularyFailure.StorageBusy => new CliException(CliErrorCode.Busy, "Notebook is busy; retry the same idempotency key."),
                    VocabularyFailure.InvalidContent => new CliException(CliErrorCode.InvalidInput, "Content violates notebook limits or invariants."),
                    _ => new CliException(CliErrorCode.Io, "Notebook operation failed. Check the app's storage status.")
                };
                return CliWire.Failure(request.Id, mapped);
            }
            catch (Exception) { return CliWire.Failure(request.Id, new CliException(CliErrorCode.Io, "Notebook operation failed. Check the app's storage status.")); }
        });
    }
}
