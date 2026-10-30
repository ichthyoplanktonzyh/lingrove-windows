namespace Cotabby.Core.LanguageLearning;

/// <summary>One request-owned instruction facade. Prompt policy and editor evidence remain
/// with the caller; local implementations may constrain lookup grammar, while endpoints keep
/// the original prompt/parser contract. Admission is checked immediately before an OS effect.</summary>
public interface ILearningInstructionEngine : IAsyncDisposable
{
    Task<string> GenerateAsync(string prompt, int output, int maximumBytes, CancellationToken token, Func<bool>? admission = null);
    Task<string> GenerateLookupAsync(string prompt, int output, int maximumBytes, bool hasContext,
        CancellationToken token, Func<bool>? admission = null) => GenerateAsync(prompt, output, maximumBytes, token, admission);
}
