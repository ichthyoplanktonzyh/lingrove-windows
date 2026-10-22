using Cotabby.Core.LanguageLearning;
using LLama.Native;
using LLama.Sampling;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>
/// The narrow LLamaSharp seam used by the operation loop. Tests replace it with a scripted
/// session so tokenization, batch boundaries, EOG, output limits, cancellation, and disposal can
/// be verified without loading GGUF weights.
/// </summary>
internal interface ILearningLocalNativeSessionFactory
{
    Task<ILearningLocalNativeSession> LoadAsync(
        LearningLocalModelConfiguration configuration,
        CancellationToken ct);
}

internal interface ILearningLocalNativeSession : IDisposable
{
    LearningGgufMetadata Metadata { get; }
    int ContextWindowTokens { get; }
    int BatchSize { get; }

    IReadOnlyList<LLamaToken> Tokenize(string text, bool allowSpecialTokens);
    void ClearMemory();
    int DecodeBatch(IReadOnlyList<LLamaToken> tokens, int offset, int count, int position, bool requestLogits);
    int DecodeToken(LLamaToken token, int position);
    bool IsEndOfGeneration(LLamaToken token);
    byte[] TokenToBytes(LLamaToken token);
    ILearningLocalSampler CreateSampler(LearningModelSamplingValues values);
}

internal interface ILearningLocalSampler : IDisposable
{
    LLamaToken Sample(int logitsIndex);
    void Accept(LLamaToken token);
}
