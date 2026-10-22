using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Win32.Text;

/// <summary>
/// Adapts Windows' ICU4C sentence-break API to the portable learning policy. Each call creates an
/// iterator for the captured source language and returns trimmed UTF-16 ranges; Core uses those
/// ranges to choose one whole sentence around the caret. This type never falls back to a punctuation
/// heuristic, because doing so would silently change which text a translation request can send.
/// </summary>
/// <remarks>
/// The parameterless constructor is inert. ICU is loaded only when <see cref="Tokenize"/> is called,
/// so composing the learning service at app startup performs no native work. The iterator owns no
/// state after the call and can safely be shared by the application composition root.
/// </remarks>
public sealed class IcuLearningSentenceTokenizer : ILearningSentenceTokenizer
{
    private readonly IIcuSentenceSegmenter _segmenter;

    /// <summary>Creates a lazy Windows ICU adapter without loading ICU during app composition.</summary>
    public IcuLearningSentenceTokenizer() : this(NativeIcuSentenceSegmenter.Instance)
    {
    }

    internal IcuLearningSentenceTokenizer(IIcuSentenceSegmenter segmenter)
    {
        ArgumentNullException.ThrowIfNull(segmenter);
        _segmenter = segmenter;
    }

    /// <inheritdoc />
    public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Enum.IsDefined(language))
        {
            throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
        }
        if (value.Length == 0 || string.IsNullOrWhiteSpace(value)) return Array.Empty<LearningTextRange>();

        var locale = IcuLocale(language);
        try
        {
            return _segmenter.Segment(value, locale);
        }
        catch (LearningTranslationException)
        {
            throw;
        }
        catch (Exception error) when (error is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or MarshalDirectiveException
            or SEHException)
        {
            // Do not expose Windows loader details or substitute weaker segmentation. The caller
            // can still let the user select text manually when this OS lacks the ICU entry point.
            throw Unavailable();
        }
    }

    private static string IcuLocale(LearningLanguage language)
    {
        // LearningLanguage.Code is a stable BCP-47-shaped identifier. ICU's C API accepts ICU
        // locale IDs, whose script separator is an underscore. ICU ships sentence-filter data for
        // these languages; enabling it keeps common abbreviations such as "Mr." inside one sentence.
        var locale = language.Code().Replace('-', '_');
        return language is LearningLanguage.English
            or LearningLanguage.German
            or LearningLanguage.Spanish
            or LearningLanguage.French
            or LearningLanguage.Italian
            or LearningLanguage.Portuguese
                ? locale + "@ss=standard"
                : locale;
    }

    private static LearningTranslationException Unavailable() =>
        new(LearningTranslationFailure.SentenceTokenizerUnavailable);
}

/// <summary>Test seam for exercising the fixed unavailable failure without changing global ICU state.</summary>
internal interface IIcuSentenceSegmenter
{
    IReadOnlyList<LearningTextRange> Segment(string value, string locale);
}

/// <summary>
/// Owns one ICU UBRK_SENTENCE handle at a time. ICU retains the supplied UTF-16 pointer for the
/// iterator lifetime, so the source string stays pinned until the handle has closed in finally.
/// </summary>
internal sealed partial class NativeIcuSentenceSegmenter : IIcuSentenceSegmenter
{
    private const int UbrkSentence = 3;
    private const int UbrkDone = -1;
    private static readonly NativeIcuSentenceSegmenter Shared = new();

    private NativeIcuSentenceSegmenter()
    {
    }

    public static NativeIcuSentenceSegmenter Instance => Shared;

    public unsafe IReadOnlyList<LearningTextRange> Segment(string value, string locale)
    {
        // ICU's UChar and .NET's char are both 16-bit UTF-16 code units. ICU returns offsets in
        // those code units, matching LearningTextRange and Windows UIA's UTF-16 positions exactly.
        fixed (char* text = value)
        {
            var status = 0; // U_ZERO_ERROR; ICU negative warning values are still successful.
            var iterator = ubrk_open(UbrkSentence, locale, text, value.Length, &status);
            if (iterator == IntPtr.Zero) throw Unavailable();

            try
            {
                if (status > 0) throw Unavailable(); // U_FAILURE(status)

                var start = ubrk_first(iterator);
                if (start != 0) throw Unavailable();

                var ranges = new List<LearningTextRange>();
                var previous = start;
                while (true)
                {
                    var boundary = ubrk_next(iterator);
                    if (boundary == UbrkDone) break;
                    if (boundary <= previous || boundary > value.Length) throw Unavailable();

                    // NLTokenizer returns the sentence token without surrounding separator spaces.
                    // ICU's sentence iterator can include those spaces in its boundary interval, so
                    // trim only edge whitespace while retaining the exact original UTF-16 offsets.
                    var tokenStart = previous;
                    var tokenEnd = boundary;
                    while (tokenStart < tokenEnd && char.IsWhiteSpace(value[tokenStart])) tokenStart++;
                    while (tokenEnd > tokenStart && char.IsWhiteSpace(value[tokenEnd - 1])) tokenEnd--;
                    if (tokenEnd > tokenStart)
                    {
                        ranges.Add(new LearningTextRange(tokenStart, tokenEnd - tokenStart));
                    }

                    previous = boundary;
                }

                if (previous != value.Length) throw Unavailable();
                return ranges.AsReadOnly();
            }
            finally
            {
                // Close even when ICU reports malformed status or an unexpected boundary. The
                // close runs before the fixed block releases the string buffer.
                ubrk_close(iterator);
            }
        }
    }

    private static LearningTranslationException Unavailable() =>
        new(LearningTranslationFailure.SentenceTokenizerUnavailable);

    [LibraryImport("icu.dll", EntryPoint = "ubrk_open", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial IntPtr ubrk_open(
        int type,
        string locale,
        char* text,
        int textLength,
        int* status);

    [LibraryImport("icu.dll", EntryPoint = "ubrk_first")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ubrk_first(IntPtr iterator);

    [LibraryImport("icu.dll", EntryPoint = "ubrk_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ubrk_next(IntPtr iterator);

    [LibraryImport("icu.dll", EntryPoint = "ubrk_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void ubrk_close(IntPtr iterator);
}
