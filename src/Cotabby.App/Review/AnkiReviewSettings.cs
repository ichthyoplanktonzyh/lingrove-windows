using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Cotabby.App.Settings;
using Cotabby.Core.Review;
using Cotabby.Core.Text;

namespace Cotabby.App.Review;

/// <summary>App-owned review preferences shared by future settings/preparation windows. Read
/// through the existing settings owner so general reset cannot leave a stale instance. Setters
/// persist only these values and notify presentation; they never apply inference settings, probe
/// Anki or read a credential. Explicit key/connection actions borrow the separate secret store.</summary>
internal sealed class AnkiReviewSettings(Func<AppSettings> current, Action persist, IAnkiCredentialStore credentials) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Address { get => FoundationPreferenceValue.StringValue(current().AnkiReviewAddressValue) ?? "http://127.0.0.1:8765"; set { current().AnkiReviewAddressValue = JsonSerializer.SerializeToElement(value); Changed(nameof(Address)); } }
    public string Deck { get => FoundationPreferenceValue.StringValue(current().AnkiReviewDeckValue) ?? "Default"; set { current().AnkiReviewDeckValue = JsonSerializer.SerializeToElement(value); Changed(nameof(Deck)); } }
    public string Model { get => FoundationPreferenceValue.StringValue(current().AnkiReviewModelValue) ?? AnkiReviewPolicy.PresetName; set { current().AnkiReviewModelValue = JsonSerializer.SerializeToElement(value); Changed(nameof(Model)); PropertyChanged?.Invoke(this, new(nameof(EffectiveMapping))); } }
    public IReadOnlyDictionary<ReviewField, string> Mapping
    {
        get
        {
            try
            {
                if (current().AnkiReviewMappingValue is { ValueKind: JsonValueKind.String } value && value.TryGetBytesFromBase64(out var data))
                    return ReviewMappingCodec.Decode(data) ?? AnkiReviewPolicy.PresetMapping;
            }
            catch (InvalidOperationException) { }
            return AnkiReviewPolicy.PresetMapping;
        }
        set { current().AnkiReviewMappingValue = JsonSerializer.SerializeToElement(ReviewMappingCodec.Encode(value)); Changed(nameof(Mapping)); PropertyChanged?.Invoke(this, new(nameof(EffectiveMapping))); }
    }
    public IReadOnlyDictionary<ReviewField, string> EffectiveMapping => Model == AnkiReviewPolicy.PresetName ? AnkiReviewPolicy.PresetMapping : Mapping;
    public bool DefaultRecognition { get => FoundationPreferenceValue.CastBoolean(current().AnkiReviewRecognitionValue) ?? true; set { current().AnkiReviewRecognitionValue = JsonSerializer.SerializeToElement(value); Changed(nameof(DefaultRecognition)); } }
    public bool DefaultGap { get => FoundationPreferenceValue.BooleanValue(current().AnkiReviewGapValue); set { current().AnkiReviewGapValue = JsonSerializer.SerializeToElement(value); Changed(nameof(DefaultGap)); } }
    public bool HasSavedDeck => FoundationPreferenceValue.StringValue(current().AnkiReviewDeckValue) is not null;
    public AnkiConnection Connection()
    {
        string? endpoint = AnkiArchiveEndpointPolicy.Normalize(Address);
        if (endpoint is null) throw new AnkiReviewException("Use a local AnkiConnect HTTP address.");
        return new(endpoint, credentials.ReadKey());
    }
    public string ReadKey() => credentials.ReadKey();
    public void SaveKey(string value) => credentials.SaveKey(value);
    private void Changed(string property)
    {
        // UserDefaults writes are best effort. Retain an acknowledged in-memory edit if the
        // Windows preference file cannot currently be saved; keys use a separate error boundary.
        try { persist(); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        PropertyChanged?.Invoke(this, new(property));
        if (property == nameof(Deck)) PropertyChanged?.Invoke(this, new(nameof(HasSavedDeck)));
    }
}
