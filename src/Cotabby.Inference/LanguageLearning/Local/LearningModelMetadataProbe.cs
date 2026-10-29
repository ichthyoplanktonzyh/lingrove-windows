using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>Bounded metadata inspection only; does not instantiate a model runtime.</summary>
public static class LearningModelMetadataProbe
{
    public static LearningGgufMetadata Read(string path, CancellationToken token) => LearningGgufHeaderReader.ReadFile(path, token);
}
