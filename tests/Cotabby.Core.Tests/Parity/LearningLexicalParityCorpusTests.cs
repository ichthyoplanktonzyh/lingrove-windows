using Cotabby.Tests.Parity;

namespace Cotabby.Core.Tests.Parity;

public sealed class LearningLexicalParityCorpusTests
{
    public static IEnumerable<object[]> CaseIds() => LearningLexicalCorpus.Cases().Select(row => new object[] { LearningLexicalCorpus.Text(row, "id") });

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void RecognizerMatchesActualMacObservationsWithRecordedPlatformText(string id)
    {
        var row = LearningLexicalCorpus.Cases().Single(row => LearningLexicalCorpus.Text(row, "id") == id);
        LearningLexicalCorpus.AssertRecognition(row, LearningLexicalCorpus.Oracle(row));
    }
}
