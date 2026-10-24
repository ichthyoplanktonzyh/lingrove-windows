using Cotabby.Core.Text;

namespace Cotabby.Core.Models;

/// <summary>One discovered file identity, adapted from the shared Mac runtime value. Picker
/// labels never change its filename or URL. Speech model discovery borrows the same value; its
/// settings surface can separately present SpeechModelCatalog's package labels.</summary>
public sealed record RuntimeModelOption(string Filename, Uri Url)
{
    public string Id => Filename;
    public string ActualModelName => Filename;
    public string DisplayName => Filename switch
    {
        "Qwen3.5-0.8B-Base.i1-Q6_K.gguf" => "Cotabby Nano", "Qwen3.5-2B-Base.i1-Q4_K_M.gguf" => "Cotabby Mini",
        "gemma-4-E2B.i1-Q6_K.gguf" => "Cotabby Base", "gemma-4-E4B.i1-Q4_K_M.gguf" => "Cotabby Pro", _ => Filename,
    };
    public bool Equals(RuntimeModelOption? other) => other is not null && Url == other.Url && CanonicalStringComparer.Instance.Equals(Filename, other.Filename);
    public override int GetHashCode() => HashCode.Combine(Url, CanonicalStringComparer.Instance.GetHashCode(Filename));
}
