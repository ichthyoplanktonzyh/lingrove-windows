namespace Cotabby.Core.Focus;

/// <summary>Admission and freshness checks for the focus owner's composition observations.</summary>
public static class CompositionEvidencePolicy
{
    public static bool IsClear(FocusedField? field) =>
        field is { Composition: InputCompositionState.Clear, CompositionSequence: > 0 };

    public static bool IsSameReceipt(FocusedField? expected, FocusedField? current) =>
        IsClear(expected) && IsClear(current)
        && expected!.CompositionSequence == current!.CompositionSequence;
}
