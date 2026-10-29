using Cotabby.Core.Focus;
using Cotabby.Core.Text;

namespace Cotabby.Core.ExpressionAssist;

/// <summary>Pure mutation arming and committed-snapshot matching. The app owns the event source,
/// clock and at most four explicit 35 ms refreshes; this value never polls or reads an editor.</summary>
public sealed class ExpressionAssistTriggerObserver
{
    private long _epoch;
    private TimeSpan _deadline;
    private int _process;
    private string _field = "";
    private long _focusEpoch;
    private string _trigger = "";

    /// <summary>Returns a new epoch and whether the last typed character warrants explicit probes.</summary>
    public (long Epoch, bool Probe) Mutation(FocusedField? field, string characters, string configured, TimeSpan now)
    {
        ++_epoch; _deadline = TimeSpan.Zero;
        if (ExpressionAssistPolicy.TriggerError(configured) is not null || field is null || field.ProcessId <= 0
            || field.FocusChangeSequence <= 0 || string.IsNullOrEmpty(field.FieldId)) return (_epoch, false);
        _process = field.ProcessId; _field = field.FieldId; _focusEpoch = field.FocusChangeSequence;
        _trigger = configured; _deadline = now + TimeSpan.FromMilliseconds(500);
        string last = new GraphemeString(configured).Suffix(1);
        bool probe = configured == "??" ? characters.EndsWith('?') || characters.EndsWith('？')
            : new GraphemeString(characters).Count > 0 && CanonicalStringComparer.Instance.Equals(new GraphemeString(characters).Suffix(1), last);
        return (_epoch, probe);
    }

    /// <summary>Matching is possible only after a mutation in this field and positive committed
    /// composition evidence. Success consumes the arming; a stale probe cannot open a session.</summary>
    public string? Observe(FocusedField? field, long epoch, TimeSpan now)
    {
        if (epoch != _epoch || _deadline == TimeSpan.Zero || now >= _deadline || field is null || field.IsSecure
            || field.ProcessId != _process || field.FieldId != _field || field.FocusChangeSequence != _focusEpoch
            || !field.HasReliableSelection || field.HasSelection || !CompositionEvidencePolicy.IsClear(field)
            || field.Text is null || field.CaretOffset < 0 || field.CaretOffset > field.Text.Length) return null;
        string? matched = ExpressionAssistPolicy.MatchedTrigger(field.PrecedingText, _trigger);
        if (matched is not null) _deadline = TimeSpan.Zero;
        return matched;
    }

    public void Cancel() { ++_epoch; _deadline = TimeSpan.Zero; }
}
