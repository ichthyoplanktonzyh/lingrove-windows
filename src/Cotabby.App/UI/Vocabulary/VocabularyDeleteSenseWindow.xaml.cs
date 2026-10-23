using System.Windows;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>Owned modal adaptation of Mac's explicit sense-delete alert; no Store authority lives here.</summary>
public partial class VocabularyDeleteSenseWindow : Window
{
    internal VocabularyDeleteSenseWindow() { InitializeComponent(); WindowTheme.Follow(this); }
    private void OnCancel(object sender, RoutedEventArgs args) => DialogResult = false;
    private void OnDelete(object sender, RoutedEventArgs args) => DialogResult = true;
}
