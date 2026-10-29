namespace Cotabby.Core.Focus;

/// <summary>Admission and freshness checks for the focus owner's composition observations.</summary>
public static class CompositionEvidencePolicy
{
    public static bool IsClear(FocusedField? field) =>
        field is { Composition: InputCompositionState.Clear, CompositionSequence: > 0 };

    public static bool IsSameReceipt(FocusedField? expected, FocusedField? current) =>
        IsClear(expected) && IsClear(current)
        && expected!.CompositionSequence == current!.CompositionSequence;

    /// <summary>Accept only matching observations around an asynchronous host text read.</summary>
    public static (InputCompositionState State, long Version) Reconcile(
        (InputCompositionState State, long Version) before,
        (InputCompositionState State, long Version) after) =>
        before == after && after.Version > 0 && after.State is InputCompositionState.Clear or InputCompositionState.Composing
            ? after : default;
}
