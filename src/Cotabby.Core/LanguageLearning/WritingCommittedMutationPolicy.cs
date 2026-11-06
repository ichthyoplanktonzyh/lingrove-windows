using Cotabby.Core.Focus;

namespace Cotabby.Core.LanguageLearning;

/// <summary>Conservative complete-field subset of Mac 3b committed mutation detection. Navigation,
/// unknown composition, truncated windows and field changes do not authorize passive capture.</summary>
public static class WritingCommittedMutationPolicy
{
    public static bool HasMutation(FocusedField? old, FocusedField? current) => old is not null && current is not null
        && old.ProcessId > 0 && old.ProcessId == current.ProcessId && !string.IsNullOrEmpty(old.FieldId)
        && string.Equals(old.FieldId, current.FieldId, StringComparison.Ordinal)
        && old.FocusChangeSequence > 0 && old.FocusChangeSequence == current.FocusChangeSequence
        && CompositionEvidencePolicy.IsClear(old) && CompositionEvidencePolicy.IsClear(current)
        && !old.IsSecure && !current.IsSecure && !old.IsTextWindowTruncated && !current.IsTextWindowTruncated
        && old.HasReliableSelection && current.HasReliableSelection && !current.HasSelection
        && !string.Equals(old.Text, current.Text, StringComparison.Ordinal);
}
