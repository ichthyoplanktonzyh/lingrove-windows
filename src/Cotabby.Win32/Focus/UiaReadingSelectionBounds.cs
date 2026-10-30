using Cotabby.Core.Focus;
using Cotabby.Core.LanguageLearning.Reading;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;
using Windows.Win32;

namespace Cotabby.Win32.Focus;

// Optional geometry of the retained exact range, not a field/window or mouse-position fallback.
// SAFEARRAY storage belongs to UIA; borrow its doubles only while locked, then destroy it here.
internal sealed unsafe partial class UiaTextEditEvidence
{
    private static ScreenRect? ReadingRangeBounds(IUIAutomationTextRange* range, Func<bool> allowed)
    {
        SAFEARRAY* rectangles = null;
        try
        {
            if (!allowed() || range is null || range->GetBoundingRectangles(&rectangles).Failed || rectangles is null || !allowed()
                || rectangles->cDims != 1 || rectangles->cbElements != sizeof(double)) return null;
            int lower = 0, upper = -1;
            if (SpeechNativeMethods.SafeArrayGetLBound(rectangles, 1, &lower).Failed || SpeechNativeMethods.SafeArrayGetUBound(rectangles, 1, &upper).Failed
                || upper < lower || (long)upper - lower >= 4096 || ((long)upper - lower + 1) % 4 != 0 || !allowed()) return null;
            void* data = null;
            if (SpeechNativeMethods.SafeArrayAccessData(rectangles, &data).Failed || data is null) return null;
            ScreenRect bounds = default;
            try
            {
                var values = new ReadOnlySpan<double>(data, (int)((long)upper - lower + 1));
                for (int i = 0; i < values.Length; i += 4)
                {
                    var box = new ScreenRect(values[i], values[i + 1], values[i + 2], values[i + 3]);
                    if (!allowed() || !ReadingVisualPolicy.Valid(box)) return null;
                    bounds = ReadingVisualPolicy.Union(bounds, box);
                }
            }
            finally { SpeechNativeMethods.SafeArrayUnaccessData(rectangles); }
            return !bounds.IsEmpty && allowed() ? bounds : null;
        }
        finally { if (rectangles is not null) SpeechNativeMethods.SafeArrayDestroy(rectangles); }
    }
}
