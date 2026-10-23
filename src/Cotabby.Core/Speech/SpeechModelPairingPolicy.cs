using Cotabby.Core.Text;

namespace Cotabby.Core.Speech;

/// <summary>Selection, admission and display share the original exact companion metadata. This
/// stateless boundary never probes files, loads models or guesses an imported decoder's native
/// compatibility. Unknown custom filenames remain the runtime's explicit responsibility.</summary>
public static class SpeechModelPairingPolicy
{
    private sealed record Package(string Model, IReadOnlyList<string> Decoders, string Label);
    private static readonly Package[] Packages =
    [
        new("Qwen3-TTS-12Hz-1.7B-Base-Q4_K_M.gguf", Array.AsReadOnly(new[] { "mmproj-Qwen3-TTS-12Hz-1.7B-Base-Q8_0.gguf" }), "1.7B"),
        new("Qwen3-TTS-12Hz-0.6B-Base.Q4_K_M.gguf", Array.AsReadOnly(new[] { "mmproj-Qwen3-TTS-12Hz-0.6B-Base-Q8_0.gguf", "Qwen3-TTS-12Hz-0.6B-Base.mmproj-Q8_0.gguf" }), "0.6B"),
    ];
    public static IReadOnlyList<string> DecoderFilenames(string modelFilename) => Packages.FirstOrDefault(p => Same(p.Model, modelFilename))?.Decoders ?? Array.Empty<string>();
    public static bool IsKnownDecoder(string filename) => Packages.Any(p => p.Decoders.Contains(filename, CanonicalStringComparer.Instance));
    public static bool IsKnownMismatch(string model, string decoder)
    {
        var expected = DecoderFilenames(model);
        return expected.Count > 0 && IsKnownDecoder(decoder) && !expected.Contains(decoder, CanonicalStringComparer.Instance);
    }
    public static string? DisplayName(string filename)
    {
        if (Packages.FirstOrDefault(p => Same(p.Model, filename)) is { } model) return $"Qwen3-TTS {model.Label} Base · Q4_K_M";
        if (Packages.FirstOrDefault(p => p.Decoders.Contains(filename, CanonicalStringComparer.Instance)) is { } decoder) return $"Qwen3-TTS {decoder.Label} audio decoder · Q8_0";
        return null;
    }
    private static bool Same(string a, string b) => CanonicalStringComparer.Instance.Equals(a, b);
}
