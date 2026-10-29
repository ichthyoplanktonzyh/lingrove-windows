using Cotabby.Tests.Speech;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;
namespace Cotabby.App.Tests;

public sealed class LocalSpeechRuntimeTests
{
    [Fact]
    public async Task RevokedPermitAfterHelperStartupPreventsTextPipeWrite()
    {
        await using var fixture = new SpeechRuntimeFixture(); int checks = 0;
        var request = new SpeechAudioRequest(new(), "fixture", LearningLanguage.English, new SpeechAudioSource.Local(fixture.Configuration), admission: () => Interlocked.Increment(ref checks) <= 2);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Runtime.StreamAsync(request, fixture.Configuration, _ => Task.CompletedTask));
        Assert.True(checks >= 3); Assert.Empty(fixture.Lines("requests"));
    }
    public static IEnumerable<object[]> Cases => SpeechRuntimeCases.Data();
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task CompleteRuntimeMatchesOriginalRealProcessObservations(string id, string expected) =>
        await SpeechRuntimeCases.RunAsync(id, expected);
}
