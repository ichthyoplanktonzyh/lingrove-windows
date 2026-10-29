using System.Text.RegularExpressions;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning.Reading;

/// <summary>Frozen selection target and lookup routing. The platform owns language-aware word
/// segmentation; its count is supplied explicitly instead of guessing unknown-language words.</summary>
public static class ReadingSelectionPolicy
{
    private static readonly Regex Terminator = new("[.!?。！？…][\"'”’»）」』】\\]\\)]*\\s*$", RegexOptions.CultureInvariant);
    private static readonly Regex Decorations = new("^[.!?。！？…]*[\"'”’»）」』】\\]\\)]*$", RegexOptions.CultureInvariant);

    public static void Validate(string text)
    {
        if (text is null || FoundationText.TrimWhitespaceAndNewlines(text).Length == 0)
            throw new ArgumentException("Select text before translating.");
        if (!LearningExplanationText.Fits(text, ModelContextBudget.MaximumTextBytes))
            throw ModelContextBudget.OversizedTarget();
    }

    public static void ValidateReceipt(ReadingSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        bool absolute = selection.SelectionStart is int start && start >= 0
            && selection.SelectionLength > 0 && start <= int.MaxValue - selection.SelectionLength;
        bool opaque = selection.SelectionAnchorId is Guid id && id != Guid.Empty;
        if (selection.Id == Guid.Empty || selection.ProcessId <= 0 || string.IsNullOrWhiteSpace(selection.FieldId)
            || selection.FocusEpoch <= 0 || absolute == opaque || (selection.SelectionStart.HasValue && !absolute)
            || (selection.SelectionAnchorId.HasValue && !opaque) || selection.Text is null
            || selection.SelectionLength <= 0 || selection.SelectionLength != selection.Text.Length
            || selection.IsSecure || !selection.IsComplete || selection.Composition != Cotabby.Core.Focus.InputCompositionState.Clear
            || selection.CompositionSequence <= 0) throw new ArgumentException("The selected occurrence could not be verified.");
        Validate(selection.Text);
    }

    public static ReadingRoute Route(ReadingSelection selection, int wordCount)
    {
        ValidateReceipt(selection);
        if (wordCount < 0) throw new ArgumentOutOfRangeException(nameof(wordCount));
        var text = FoundationText.TrimWhitespaceAndNewlines(selection.Text);
        bool one = wordCount <= 1;
        if (selection.Context is { } invalid && !ValidContext(invalid, selection.Text))
            throw new ArgumentException("The selected occurrence could not be verified.");
        if (selection.Context is { } context && CoversContext(context))
        {
            bool complete = Complete(context.Left) && Complete(context.Right);
            if (complete && (!one || Hint(context.Text, wordCount))) return new(true, ReadingRouteReason.WholeSentence, null, one);
            bool unknown = context.Left is not (ReadingBoundary.Crop or ReadingBoundary.Deadline)
                && context.Right is not (ReadingBoundary.Crop or ReadingBoundary.Deadline) && !complete;
            if (unknown && !one)
            {
                if (context.Method == ReadingContextMethod.Block && context.Left == ReadingBoundary.LayoutBlock && context.Right == ReadingBoundary.LayoutBlock)
                    return new(true, ReadingRouteReason.WholeBlock, null, one);
                if (Hint(context.Text, wordCount)) return new(true, ReadingRouteReason.WholeUnverifiedSentence, null, one);
            }
        }
        if (selection.Context is null && Hint(text, wordCount)) return new(true, ReadingRouteReason.TerminalText, null, one);
        return new(false, selection.Context is null ? ReadingRouteReason.StandaloneUnit : ReadingRouteReason.ContextualUnit, text, one);
    }

    public static bool ValidContext(ReadingContext context, string expected)
    {
        if (context.Text is null || context.TargetStart < 0 || context.TargetLength <= 0 ||
            context.TargetStart > context.Text.Length || context.TargetLength > context.Text.Length - context.TargetStart ||
            context.Text.Length - context.TargetLength > 1_024 || context.TargetLength > 3_072 ||
            !EditorTextRangePolicy.IsValid(context.Text, context.TargetStart, context.TargetLength)) return false;
        if (expected is null) return false;
        string target = FoundationText.TrimWhitespaceAndNewlines(expected);
        return target.Length > 0 && context.TargetLength == target.Length
            && context.Text.AsSpan(context.TargetStart, context.TargetLength).SequenceEqual(target.AsSpan());
    }

    public static bool CoversContext(ReadingContext context)
    {
        if (context.TargetStart < 0 || context.TargetLength < 0 || context.TargetStart > context.Text.Length ||
            context.TargetLength > context.Text.Length - context.TargetStart) return false;
        var before = context.Text[..context.TargetStart];
        if (new GraphemeString(before).Any(g => !g.IsWhitespace)) return false;
        var after = FoundationText.TrimWhitespaceAndNewlines(context.Text[(context.TargetStart + context.TargetLength)..]);
        return after.Length == 0 || Decorations.IsMatch(after);
    }

    private static bool Complete(ReadingBoundary boundary) => boundary is ReadingBoundary.Container or ReadingBoundary.Sentence;
    private static bool Hint(string text, int words)
    {
        if (Terminator.IsMatch(text)) return true;
        var trimmed = FoundationText.TrimWhitespaceAndNewlines(text);
        return words > 1 && (trimmed.EndsWith(':') || trimmed.EndsWith('：'));
    }
}
