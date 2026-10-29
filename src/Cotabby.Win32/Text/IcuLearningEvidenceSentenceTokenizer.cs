using System.Runtime.InteropServices;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Win32.Text;

/// <summary>
/// Platform sentence evidence for frozen learning captures. It reuses the bounded, pinned ICU
/// iterator without trimming separator spaces, preserving the Mac example/receipt boundary.
/// The caller runs this stateless adapter on a worker; it owns no focus or editor handle.
/// </summary>
public sealed class IcuLearningEvidenceSentenceTokenizer : ILearningEvidenceSentenceTokenizer
{
    public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language)
    {
        if (!Enum.IsDefined(language)) throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        return TokenizeCore(value, IcuLearningSentenceTokenizer.IcuLocale(language));
    }
    public IReadOnlyList<LearningTextRange> TokenizeUnknown(string value) => TokenizeCore(value, "");
    private static IReadOnlyList<LearningTextRange> TokenizeCore(string value, string locale)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return Array.Empty<LearningTextRange>();
        try
        {
            return NativeIcuSentenceSegmenter.Instance.SegmentIncludingSeparators(value, locale);
        }
        catch (LearningTranslationException) { throw; }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or MarshalDirectiveException or SEHException)
        {
            throw IcuLearningSentenceTokenizer.Unavailable();
        }
    }
}
