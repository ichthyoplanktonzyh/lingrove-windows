using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using static Cotabby.Core.LanguageLearning.Reading.ReadingVisualPolicy;

namespace Cotabby.App.Reading;

/// <summary>3b VisualReader: one captured frame, at most two original-resolution OCR crops.
/// The caller owns the origin lease; every await revalidates it. No second screenshot, text repair,
/// model, clipboard or persistent image belongs to this coordinator.</summary>
internal sealed partial class ReadingVisualReader(IReadingOcr ocr, ReadingVisualContext context)
{
    internal sealed record Result(string Text, ScreenRect Anchor, ReadingContext? Context, int Passes);
    private sealed record Candidate(ReadingVisualWord Word, ScreenRect Anchor, ReadingVisualContext.Block? Block, bool NeedsExpansion);
    internal async Task<Result?> ReadAsync(ReadingBitmap frame, ReadingVisualFrame geometry,
        Func<CancellationToken, Task<bool>> current, CancellationToken token, TimeSpan? remaining = null)
    {
        async Task Check()
        { token.ThrowIfCancellationRequested(); if (!await current(token)) throw new OperationCanceledException(); token.ThrowIfCancellationRequested(); }
        await Check();
        var bounds = geometry.Bounds;
        if (!Contains(bounds, geometry.X, geometry.Y) || frame.Width != bounds.Width || frame.Height != bounds.Height
            || frame.Bgra.LongLength != (long)frame.Width * frame.Height * 4) throw new ArgumentException("Invalid OCR frame.");
        var first = IntegralInside(Centered(geometry.X, geometry.Y, 720 * geometry.Scale, 240 * geometry.Scale, bounds), bounds);
        var expansion = bounds; Candidate? accepted = null; int passes = 0;
        var watch = System.Diagnostics.Stopwatch.StartNew(); var budget = remaining ?? TimeSpan.FromSeconds(12);
        for (int pass = 0; pass < (first == bounds ? 1 : 2); pass++)
        {
            await Check(); var time = budget - watch.Elapsed;
            if (time <= TimeSpan.Zero) { if (accepted is not null) break; throw new TimeoutException("Reading OCR timed out."); }
            var region = pass == 0 ? first : expansion;
            var crop = Crop(frame, bounds, region);
            try
            {
                passes++;
                IReadOnlyList<ReadingOcrLine> lines;
                using var passCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                passCancellation.CancelAfter(time);
                try { lines = await ocr.RecognizeAsync(crop, passCancellation.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested && passCancellation.IsCancellationRequested)
                { await Check(); if (accepted is not null) break; throw new TimeoutException("Reading OCR timed out."); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) when (accepted is not null) { await Check(); break; }
                await Check();
                var extraction = ReadingVisualText.FromWindows(lines, crop.Width, crop.Height);
                var candidate = await Task.Run(() => Resolve(extraction, geometry, region, crop.Width, crop.Height), CancellationToken.None);
                await Check();
                if (candidate is null) continue;
                accepted = Preferred(candidate, accepted);
                if (!candidate.NeedsExpansion) break;
                var block = candidate.Block?.Context is null ? (ScreenRect?)null : Offset(candidate.Block.Bounds, region);
                expansion = IntegralInside(Expanded(first, block, candidate.Block?.Context, bounds, geometry.Scale), bounds);
            }
            finally { Array.Clear(crop.Bgra); }
        }
        await Check(); return accepted is null ? null : new(accepted.Word.Text, accepted.Anchor, accepted.Block?.Context, passes);
    }
    private Candidate? Resolve(ReadingVisualText extraction, ReadingVisualFrame frame, ScreenRect region, int width, int height)
    {
        var word = Hit(frame.X - region.X, frame.Y - region.Y, extraction); if (word is null) return null;
        var block = context.Resolve(word, extraction, width, height);
        var anchor = Offset(word.Bounds, region); var box = block is null ? anchor : Offset(block.Bounds, region);
        var inner = new ScreenRect(region.X + 28 * frame.Scale, region.Y + 28 * frame.Scale,
            region.Width - 56 * frame.Scale, region.Height - 56 * frame.Scale);
        return new(word, anchor, block, block?.Context is null || !Complete(block.Context) && !Contains(inner, box));
    }
    private static Candidate Preferred(Candidate candidate, Candidate? previous)
    {
        if (previous is null) return candidate;
        var overlap = Intersect(previous.Anchor, candidate.Anchor);
        double area = Math.Min(previous.Anchor.Width * previous.Anchor.Height, candidate.Anchor.Width * candidate.Anchor.Height);
        if (!string.Equals(previous.Word.Text, candidate.Word.Text, StringComparison.Ordinal) || overlap.Width * overlap.Height < area * .5) return previous;
        // Windows supplies no confidence; retain the source comparison when a provider has it.
        if (candidate.Block?.Confidence is double confidence && previous.Block?.Confidence is double oldConfidence
            && confidence < .5 && oldConfidence - confidence > .3) return previous;
        if (Complete(candidate.Block?.Context) != Complete(previous.Block?.Context)) return Complete(candidate.Block?.Context) ? candidate : previous;
        return (candidate.Block?.Context?.Text.Length ?? 0) >= (previous.Block?.Context?.Text.Length ?? 0) ? candidate : previous;
    }
    private static ScreenRect Offset(ScreenRect local, ScreenRect region) => local with { X = local.X + region.X, Y = local.Y + region.Y };
    internal static ReadingBitmap Crop(ReadingBitmap frame, ScreenRect bounds, ScreenRect region)
    {
        if (!Contains(bounds, region) || !ReadingRegionPolicy.IsValidBounds(region)) throw new ArgumentException("Invalid OCR crop.");
        int x = checked((int)(region.X - bounds.X)), y = checked((int)(region.Y - bounds.Y));
        int width = checked((int)region.Width), height = checked((int)region.Height); byte[] bytes = new byte[checked(width * height * 4)];
        try
        {
            for (int row = 0; row < height; row++) Buffer.BlockCopy(frame.Bgra, checked(((y + row) * frame.Width + x) * 4), bytes, row * width * 4, width * 4);
            return new(width, height, bytes);
        }
        catch { Array.Clear(bytes); throw; }
    }
}
