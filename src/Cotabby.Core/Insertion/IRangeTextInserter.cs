using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Insertion;

/// <summary>A separate range edit boundary; ordinary autocomplete/backspace admission is unchanged.
/// The adapter selects and acknowledges the exact anchored source before dispatching one edit.
/// An uncertain result must never be retried, rolled back or interpreted as an editor receipt.</summary>
public interface IRangeTextInserter
{
    Task<RangeEditDispatch> ReplaceRangeAsync(FocusedField target, LearningTextRange range, string source,
        string replacement, Func<bool> permit, CancellationToken cancellationToken);
}

public enum RangeEditDispatch { Rejected, Dispatched, Uncertain }
