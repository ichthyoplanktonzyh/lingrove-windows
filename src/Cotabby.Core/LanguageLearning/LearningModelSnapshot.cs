using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning;

public enum LearningModelBackend { Local, Endpoint }

/// <summary>
/// Frozen request identity from Mac LearningModelSettings. Each role's settings owner supplies its
/// own snapshot; a credential revision is identity, never a copied API key. This value owns no
/// provider, native allocation or persisted preferences. Endpoint transport/consent stays outside it.
/// Settings must supply installed model names and already-normalized context/output preferences;
/// ForExplanation projects role fields, not the unported full settings/power-selection machinery.
/// </summary>
public sealed record LearningModelSnapshot
{
    public LearningModelBackend Backend { get; init; } = LearningModelBackend.Local;
    public string LocalModel { get; init; } = "";
    public LearningChatTemplate Template { get; init; } = LearningChatTemplate.Automatic;
    public string BaseUrl { get; init; } = "";
    public string EndpointModel { get; init; } = "";
    public int CredentialRevision { get; init; }
    public int ContextWindowTokens { get; init; } = 8_192;
    public LearningTranslationStyle TranslationStyle { get; init; } = new();
    public LearningModelSamplingOverrides SamplingOverrides { get; init; } = new();
    public int? MaximumOutputTokens { get; init; }

    /// <summary>
    /// Mac settings project only the active destination and this role's parameters. Clearing
    /// dormant fields prevents editing a future endpoint or translation style from retiring a
    /// local explanation. This is a value projection, not endpoint consent or model validation.
    /// </summary>
    public LearningModelSnapshot ForExplanation() => this with
    {
        LocalModel = Backend == LearningModelBackend.Endpoint ? "" : LocalModel,
        Template = Backend == LearningModelBackend.Endpoint ? LearningChatTemplate.Automatic : Template,
        BaseUrl = Backend == LearningModelBackend.Local ? "" : BaseUrl,
        EndpointModel = Backend == LearningModelBackend.Local ? "" : EndpointModel,
        CredentialRevision = Backend == LearningModelBackend.Local ? 0 : CredentialRevision,
        TranslationStyle = new(),
        SamplingOverrides = Backend == LearningModelBackend.Endpoint ? SamplingOverrides.EndpointOnly : SamplingOverrides.Normalized,
    };

    /// <summary>Mac allocation identity excludes request-only style, sampler and output controls.</summary>
    public bool UsesSameRuntime(LearningModelSnapshot other) =>
        (this with { TranslationStyle = new(), SamplingOverrides = new(), MaximumOutputTokens = null })
        == (other with { TranslationStyle = new(), SamplingOverrides = new(), MaximumOutputTokens = null });

    public bool Equals(LearningModelSnapshot? other) => other is not null && Backend == other.Backend
        && Same(LocalModel, other.LocalModel) && Template == other.Template && Same(BaseUrl, other.BaseUrl)
        && Same(EndpointModel, other.EndpointModel) && CredentialRevision == other.CredentialRevision
        && ContextWindowTokens == other.ContextWindowTokens && TranslationStyle.Register == other.TranslationStyle.Register
        && Same(TranslationStyle.CustomInstruction, other.TranslationStyle.CustomInstruction)
        && SamplingOverrides == other.SamplingOverrides && MaximumOutputTokens == other.MaximumOutputTokens;
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Backend); hash.Add(LocalModel, CanonicalStringComparer.Instance);
        hash.Add(Template); hash.Add(BaseUrl, CanonicalStringComparer.Instance); hash.Add(EndpointModel, CanonicalStringComparer.Instance);
        hash.Add(CredentialRevision); hash.Add(ContextWindowTokens); hash.Add(TranslationStyle.Register);
        hash.Add(TranslationStyle.CustomInstruction, CanonicalStringComparer.Instance); hash.Add(SamplingOverrides); hash.Add(MaximumOutputTokens);
        return hash.ToHashCode();
    }
    private static bool Same(string left, string right) => CanonicalStringComparer.Instance.Equals(left, right);
}
