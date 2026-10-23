using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Cotabby.Win32.Vocabulary;

namespace Cotabby.App.UI.Vocabulary;

public partial class VocabularyEntryEditorWindow
{
    private WindowsVocabularyFileDrop? _fileDrop;
    private void AttachFileDrop()
    {
        if (_retired || _fileDrop is not null || HwndSource.FromVisual(this) is not HwndSource source) return;
        _fileDrop = new(source, (path, clientPixels) =>
        {
            if (!IsEnabled || !IsVisible || !CanImport() || source.CompositionTarget is null || source.RootVisual is not Visual root) return;
            if (ContainsDropPoint(ExplanationDropArea, root, clientPixels, source.CompositionTarget.TransformFromDevice)) ImportImageFile(path);
        });
    }
    /// <summary>Native drop coordinates are client pixels; hit testing consumes root-visual DIPs.</summary>
    internal static bool ContainsDropPoint(FrameworkElement area, Visual root, Point clientPixels, Matrix fromDevice)
    {
        if (!area.IsVisible) return false;
        Point point = fromDevice.Transform(clientPixels);
        // A transformed rectangle alone includes content clipped beneath the scroll viewport
        // or the Save/Cancel footer. WPF hit testing respects clipping and the actual top visual.
        for (DependencyObject? hit = VisualTreeHelper.HitTest(root, point)?.VisualHit; hit is not null; hit = VisualTreeHelper.GetParent(hit))
            if (ReferenceEquals(hit, area)) return true;
        return false;
    }
}
