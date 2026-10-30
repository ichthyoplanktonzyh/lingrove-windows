namespace Cotabby.Core.Suggestions.Streaming;

/// <summary>UI-owner state for latest-wins partials. An already scheduled dispatcher drain survives
/// generation changes; finalization prevents old partials from reviving a completed offer.</summary>
public sealed class SuggestionStreamingState
{
    public readonly record struct Pending(string Text, long WorkId);
    public Pending? PendingPartial { get; private set; }
    public bool IsDrainScheduled { get; private set; }
    public string? RenderedText { get; private set; }
    public bool IsFinalized { get; private set; }
    public void BeginGeneration() { IsFinalized = false; RenderedText = null; PendingPartial = null; }
    public bool Enqueue(string text, long workId)
    {
        if (IsFinalized) return false;
        PendingPartial = new(text, workId);
        if (IsDrainScheduled) return false;
        IsDrainScheduled = true; return true;
    }
    public Pending? Drain() { IsDrainScheduled = false; var value = PendingPartial; PendingPartial = null; return value; }
    public bool CanRender(string text) => StreamedGhostTextPolicy.IsRenderableExtension(text, RenderedText);
    public void RecordRendered(string text) => RenderedText = text;
    public void ResetRenderedText() => RenderedText = null;
    public void FinishGeneration() { IsFinalized = true; PendingPartial = null; }
    public void ClearSession() { FinishGeneration(); RenderedText = null; }
}
