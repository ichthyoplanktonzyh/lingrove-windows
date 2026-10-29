using Cotabby.Core.Review;
using F = Cotabby.Core.Vocabulary.Frozen;

namespace Cotabby.App.Review;

/// <summary>Canonical sessions borrow the same connection owner and operation lease. The legacy
/// notebook guard remains on legacy entry points and is never changed by this adapter.</summary>
internal sealed partial class AnkiReviewController
{
    internal Task<ReviewOperationScope> CanonicalScopeAsync(CancellationToken token) => OperationScopeCoreAsync(token);
    internal Task CanonicalVerifyAsync(ReviewOperationScope scope, CancellationToken token) => VerifyCoreAsync(scope, token);
    internal Task<IReadOnlyList<string>> CanonicalFieldsAsync(string model, AnkiConnection connection, bool create, string? profile, CancellationToken token, Func<Task>? beforeWrite = null) => FieldsCoreAsync(model, connection, create, profile, token, beforeWrite);
    internal Task<AnkiRemoteNote?> CanonicalNoteAsync(long id, AnkiConnection connection, CancellationToken token) => NoteCoreAsync(id, connection, token);
    internal void BeginCanonical(Guid senseId, Guid? cardId) => BeginCore(senseId, cardId);
    internal Task UpdateCanonicalPresetTemplateAsync(CancellationToken token, Func<Task> beforeWrite) => UpdatePresetTemplateCoreAsync(token, beforeWrite);
    internal async Task VerifyCanonicalBindingAsync(F.AnkiNoteBinding binding, AnkiConnection connection, CancellationToken token)
    {
        if (!Same(binding.Endpoint, connection.Endpoint) || !Same(await ProfileAsync(connection, token), binding.Profile))
            throw new AnkiReviewException("Switch to the Anki profile associated with this sense.");
    }
}
