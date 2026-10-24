using Cotabby.Tests.Speech;
namespace Cotabby.App.Tests;

public sealed class LocalSpeechRuntimeTests
{
    public static IEnumerable<object[]> Cases => SpeechRuntimeCases.Data();
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CompleteRuntimeMatchesOriginalRealProcessObservations(string id, string expected) =>
        await SpeechRuntimeCases.RunAsync(id, expected);
}
