using System.Windows;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Owned counterpart of Mac's destructive replacement alert. Cancel is the default;
/// this view returns intent only and owns no source bytes, Store or archive transaction.</summary>
public partial class VocabularyRestoreConfirmationWindow : Window
{
    private readonly Action? _accepted;
    internal VocabularyRestoreConfirmationWindow(Action? accepted = null)
    { _accepted = accepted; InitializeComponent(); WindowTheme.Follow(this); }
    private void OnCancel(object sender, RoutedEventArgs args) => DialogResult = false;
    private void OnRestore(object sender, RoutedEventArgs args)
    {
        // Report intent synchronously, before closing the modal frame can retire its owner.
        // The notebook callback, not this view, admits and owns the actual Store operation.
        _accepted?.Invoke(); DialogResult = true;
    }
}
