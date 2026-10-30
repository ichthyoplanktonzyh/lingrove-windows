using System.ComponentModel;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.App.Review;

/// <summary>One settings section owns the original SwiftUI key/saved-key/error state. Its view
/// controls appearance/focus/visibility; the shared feature owns settings, requests and Store.
/// Reading the key is an explicit appearance action. This boundary has no WPF control or startup
/// behavior, making the exact key-change guard and failure ordering independently observable.</summary>
internal sealed class AnkiReviewSettingsInteraction(AnkiReviewFeature feature) : INotifyPropertyChanged
{
    internal string Key { get; set; } = "";
    private string _savedKey = "";
    internal string? KeyError { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed() => PropertyChanged?.Invoke(this, new(null));
    internal void ReadKeyOnAppearance()
    {
        try { Key = feature.Settings.ReadKey(); _savedKey = Key; }
        catch (AnkiReviewException error) { KeyError = feature.Controller.DisplayFailure(error); }
        Changed();
    }
    internal void SaveKey()
    {
        if (CanonicalStringComparer.Instance.Equals(Key, _savedKey)) return;
        try
        {
            feature.Settings.SaveKey(Key); _savedKey = Key; KeyError = null;
            feature.Controller.InvalidateConnection();
        }
        catch (AnkiReviewException error) { KeyError = feature.Controller.DisplayFailure(error); }
        Changed();
    }
    internal Task TestConnectionAsync()
    {
        SaveKey();
        return KeyError is null ? feature.RunAsync(ct => feature.Controller.TestConnectionAsync(ReviewLayoutPolicy.Model, ct)) : Task.CompletedTask;
    }
    internal void ClearEphemeralKey()
    { Key = ""; _savedKey = ""; }
}
