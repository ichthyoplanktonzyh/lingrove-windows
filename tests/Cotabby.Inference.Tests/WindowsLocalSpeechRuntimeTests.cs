using Cotabby.Tests.Speech;
using Cotabby.Win32.Speech;
namespace Cotabby.Inference.Tests;

public sealed class WindowsLocalSpeechRuntimeTests
{
    public static IEnumerable<object[]> Cases => SpeechRuntimeCases.Data();
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task NativePipesJobAndHelperMatchOriginalRuntimeObservations(string id, string expected) =>
        await SpeechRuntimeCases.RunAsync(id, expected, _ => new WindowsSpeechProcessFactory());
}
