using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;

namespace Cotabby.App.Reading;

internal sealed partial class ReadingVisualReader
{
    /// <summary>Mac 3b selection branch. Both OCR passes independently align the original native
    /// target; ambiguity or a changed spatial occurrence discards all visual enrichment.</summary>
    internal async Task<Result?> ReadSelectionAsync(ReadingBitmap frame, ReadingVisualFrame geometry, ReadingSelection selection,
        ReadingSelectionOcrPolicy policy, Func<CancellationToken, Task<bool>> current, CancellationToken token, TimeSpan? remaining = null)
    {
        async Task Check()
        { token.ThrowIfCancellationRequested(); if (!await current(token)) throw new OperationCanceledException(); token.ThrowIfCancellationRequested(); }
        await Check(); var bounds = geometry.Bounds; double scale = geometry.Scale;
        if (ReadingGesturePolicy.SearchBounds(selection, bounds, DateTimeOffset.UtcNow, scale) is not { } selected) return null;
        if (frame.Width != bounds.Width || frame.Height != bounds.Height || frame.Bgra.LongLength != (long)frame.Width * frame.Height * 4)
            throw new ArgumentException("Invalid OCR frame.");
        double x = selected.X + selected.Width / 2, y = selected.Y + selected.Height / 2;
        var first = IntegralInside(Centered(x, y, Math.Max(720 * scale, selected.Width + 56 * scale), Math.Max(240 * scale, selected.Height + 56 * scale), bounds), bounds);
        var expansion = bounds; ReadingSelectionOcrPolicy.Match? accepted = null; int passes = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew(); var budget = remaining ?? TimeSpan.FromSeconds(12);
        for (int pass = 0; pass < (first == bounds ? 1 : 2); pass++)
        {
            await Check(); var time = budget - watch.Elapsed; if (time <= TimeSpan.Zero) break;
            var region = pass == 0 ? first : expansion; var crop = Crop(frame, bounds, region);
            try
            {
                passes++; IReadOnlyList<ReadingOcrLine> lines;
                using var passCancellation = CancellationTokenSource.CreateLinkedTokenSource(token); passCancellation.CancelAfter(time);
                try { lines = await ocr.RecognizeAsync(crop, passCancellation.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && passCancellation.IsCancellationRequested)
                { await Check(); if (accepted is not null) break; throw new TimeoutException("Reading OCR timed out."); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (accepted is not null) { await Check(); break; }
                await Check();
                var extraction = ReadingVisualText.FromWindows(lines, crop.Width, crop.Height);
                var alignment = await Task.Run(() => policy.Align(selection, extraction, region, crop.Width, crop.Height, scale), CancellationToken.None);
                await Check();
                if (alignment.Reason == ReadingSelectionOcrPolicy.Reason.Ambiguous) return null;
                if (alignment.Match is not { } match) continue;
                if (accepted is not null && !SameSelectionOccurrence(accepted.TargetBounds, match.TargetBounds, scale)) return null;
                accepted = accepted is null || Complete(match.Context) != Complete(accepted.Context)
                    ? Complete(match.Context) || accepted is null ? match : accepted
                    : match.Context.Text.Length >= accepted.Context.Text.Length ? match : accepted;
                var inner = new ScreenRect(region.X + 28 * scale, region.Y + 28 * scale, region.Width - 56 * scale, region.Height - 56 * scale);
                if (Complete(match.Context) || Contains(inner, match.Bounds)) break;
                expansion = IntegralInside(Expanded(first, match.Bounds, match.Context, bounds, scale), bounds);
            }
            finally { Array.Clear(crop.Bgra); }
        }
        await Check(); return accepted is null ? null : new(selection.Text, selection.Anchor, accepted.Context, passes);
    }
    private static bool SameSelectionOccurrence(ScreenRect first, ScreenRect second, double scale)
    {
        var overlap = Intersect(first, second); double area = Math.Min(first.Width * first.Height, second.Width * second.Height);
        double dx = first.X + first.Width / 2 - second.X - second.Width / 2, dy = first.Y + first.Height / 2 - second.Y - second.Height / 2;
        return !overlap.IsEmpty && overlap.Width * overlap.Height >= area * .8 && Math.Sqrt(dx * dx + dy * dy) <= Math.Max(4 * scale, Math.Min(first.Width, second.Width) * .35);
    }
}
