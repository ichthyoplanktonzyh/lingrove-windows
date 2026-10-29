namespace Cotabby.Core.LanguageLearning;

/// <summary>A request-order reservation, acquired before asynchronous saved lookup. It owns no
/// model or transport until CreateEngine is called; disposal releases the reservation.</summary>
public interface ILearningExplanationAdmission : IDisposable
{
    CancellationToken Token { get; }
    ILearningExplanationEngine CreateEngine(LearningModelSnapshot profile);
}
