using System.Text.Json;
using Cotabby.Core.Speech;
using Cotabby.Core.Text;

namespace Cotabby.App.Speech;

/// <summary>The speech specialization of Mac's ModelParameterSettings. Its owning settings
/// instance retains one per-destination map for its lifetime. A failed encoding leaves both the
/// map and preferences intact; publication belongs to the caller after this commit succeeds.</summary>
internal sealed class SpeechParameterSettings
{
    private Dictionary<string, SpeechGenerationProfile> _profiles;
    private readonly Action<byte[]> _save;
    internal SpeechParameterSettings(JsonElement? stored, Action<byte[]> save)
    {
        _save = save; IReadOnlyDictionary<string, SpeechGenerationProfile>? decoded = null;
        if (stored is { ValueKind: JsonValueKind.String } value && value.TryGetBytesFromBase64(out var bytes)) decoded = SpeechProfileCodec.Decode(bytes);
        _profiles = new(decoded ?? new Dictionary<string, SpeechGenerationProfile>(), CanonicalStringComparer.Instance);
    }
    internal SpeechGenerationProfile? Profile(string identity) => _profiles.GetValueOrDefault(identity);
    internal bool Set(SpeechGenerationProfile profile, string identity)
    {
        if (_profiles.TryGetValue(identity, out var existing) && Same(existing, profile)) return false;
        var next = new Dictionary<string, SpeechGenerationProfile>(_profiles, CanonicalStringComparer.Instance) { [identity] = profile };
        byte[] encoded;
        try { encoded = SpeechProfileCodec.Encode(next); } catch (ArgumentException) { return false; }
        _profiles = next; _save(encoded); return true;
    }
    private static bool Same(SpeechGenerationProfile a, SpeechGenerationProfile b) => a.Sampling == b.Sampling
        && CanonicalStringComparer.Instance.Equals(a.ReferenceAudioPath, b.ReferenceAudioPath)
        && CanonicalStringComparer.Instance.Equals(a.EndpointVoice, b.EndpointVoice);
}
