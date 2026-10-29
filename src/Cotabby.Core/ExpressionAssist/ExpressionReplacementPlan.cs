using Cotabby.Core.Insertion;

namespace Cotabby.Core.ExpressionAssist;

/// <summary>Portable planning only: no selection change, clipboard or input is performed here.</summary>
public sealed record ExpressionReplacementPlan(int Backspaces, string Replacement, string ExpectedValue)
{
    public static ExpressionReplacementPlan? Create(ExpressionEditorReceipt receipt, string? replacement)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!ExpressionEditorReceiptPolicy.MatchesOriginal(receipt, receipt.Original)
            || !InsertionTargetPolicy.IsValidInsertionText(replacement)) return null;
        // The frozen UI uses empty replacement only to remove its own trigger on cancel.
        if (replacement!.Length == 0 && !receipt.IsTrigger) return null;
        if (receipt.IsTrigger && receipt.Source.Any(c => c < 0x20 || c > 0x7e)) return null;
        int resultingLength = receipt.Original.Value.Length - receipt.ReplacementRange.Length + replacement.Length;
        if (resultingLength > ExpressionEditorReceiptPolicy.MaximumValueUtf16Units) return null;
        int backspaces = receipt.IsTrigger ? receipt.ReplacementRange.Length : 0;
        string expected = receipt.Original.Value.Remove(receipt.ReplacementRange.Start, receipt.ReplacementRange.Length)
            .Insert(receipt.ReplacementRange.Start, replacement);
        return new(backspaces, replacement, expected);
    }
}
