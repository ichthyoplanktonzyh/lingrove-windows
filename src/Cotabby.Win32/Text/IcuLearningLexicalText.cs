using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Win32.Text;

/// <summary>
/// Adapts platform text primitives behind the Mac lexical index. Scalar-by-scalar full ICU casing
/// preserves Swift's expansions without contextual final sigma; UBRK_WORD replaces NLTokenizer.
/// No suffix, whitespace-only word splitter or unavailable-provider heuristic is substituted.
/// Each call owns its temporary native buffers/iterator; the app can share this inert adapter.
/// </summary>
public sealed class IcuLearningLexicalText : ILearningLexicalText
{
    private const int Alphabetic = 0;
    private const int Uppercase = 30;
    private readonly IIcuLexicalApi _api;
    public IcuLearningLexicalText() : this(NativeIcuLexicalApi.Instance) { }
    internal IcuLearningLexicalText(IIcuLexicalApi api) => _api = api;

    /// <summary>Full Swift-style Unicode lowercase without lexical key's apostrophe or whitespace projection.</summary>
    public string Lowercase(string value)
    {
        Validate(value);
        return value.Length == 0 ? string.Empty : Run(() => _api.ChangeCase(value, upper: false));
    }

    public string Key(string value)
    {
        Validate(value);
        if (value.Length == 0) return string.Empty;
        return Run(() => LearningUnitPolicy.CollapseWhitespace(
            _api.ChangeCase(value.Normalize(NormalizationForm.FormC), upper: false).Replace('’', '\'')));
    }

    public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language)
    {
        if (!Enum.IsDefined(language)) throw Invalid();
        return WordsCore(value, language.Code().Replace('-', '_'));
    }

    /// <summary>Uses ICU's root locale when the reading language is unknown; no language is inferred.</summary>
    public IReadOnlyList<LearningDictionaryWord> WordsUnknown(string value) => WordsCore(value, "");

    private IReadOnlyList<LearningDictionaryWord> WordsCore(string value, string locale)
    {
        Validate(value);
        if (value.Length == 0) return Array.Empty<LearningDictionaryWord>();
        return Run<IReadOnlyList<LearningDictionaryWord>>(() =>
        {
            var words = new List<LearningDictionaryWord>();
            var boundaries = StringInfo.ParseCombiningCharacters(value).ToHashSet();
            boundaries.Add(value.Length);
            int previousEnd = 0;
            foreach (var range in _api.WordRanges(value, locale))
            {
                if (range.Location != previousEnd || range.Length <= 0 || range.Location > value.Length
                    || range.Length > value.Length - range.Location || !boundaries.Contains(range.Location)
                    || !boundaries.Contains(range.End))
                    throw new LearningLexicalException(LearningLexicalFailure.InvalidWordRanges);
                previousEnd = range.End;
                var word = value.Substring(range.Location, range.Length);
                if (Encoding.UTF8.GetByteCount(word) <= 256 && Elements(word).Any(IsLetter))
                    words.Add(new LearningDictionaryWord(word, range));
            }
            if (previousEnd != value.Length) throw new LearningLexicalException(LearningLexicalFailure.InvalidWordRanges);
            return words.AsReadOnly();
        });
    }

    public bool IsPatternText(string value)
    {
        Validate(value);
        return Run(() => Elements(value).All(element => IsLetter(element)
            || char.IsWhiteSpace(element, 0) || element is "'" or "’"));
    }

    public bool FirstIsUppercase(string value)
    {
        Validate(value);
        if (value.Length == 0) return false;
        return Run(() =>
        {
            var first = StringInfo.GetNextTextElement(value);
            var runes = first.EnumerateRunes().ToArray();
            if (runes.Length == 1 && _api.HasBinaryProperty(runes[0].Value, Uppercase)) return true;
            // Swift Character's multi-scalar path compares full case mappings using canonical
            // string equality; general-category Lu alone misses its Unicode Uppercase property.
            var original = first.Normalize(NormalizationForm.FormC);
            var upper = _api.ChangeCase(first, upper: true).Normalize(NormalizationForm.FormC);
            var lower = _api.ChangeCase(first, upper: false).Normalize(NormalizationForm.FormC);
            return string.Equals(original, upper, StringComparison.Ordinal)
                && !string.Equals(original, lower, StringComparison.Ordinal);
        });
    }

    private bool IsLetter(string element) => _api.HasBinaryProperty(Rune.GetRuneAt(element, 0).Value, Alphabetic);
    private static IEnumerable<string> Elements(string value)
    {
        var elements = StringInfo.GetTextElementEnumerator(value);
        while (elements.MoveNext()) yield return elements.GetTextElement();
    }
    private static void Validate(string value)
    {
        if (value is null) throw Invalid();
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != OperationStatus.Done) throw Invalid();
            remaining = remaining[consumed..];
        }
    }
    private static T Run<T>(Func<T> action)
    {
        try { return action(); }
        catch (LearningLexicalException) { throw; }
        catch (ArgumentException) { throw Invalid(); }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or MarshalDirectiveException or SEHException)
        { throw Unavailable(); }
    }
    private static LearningLexicalException Invalid() => new(LearningLexicalFailure.InvalidInput);
    private static LearningLexicalException Unavailable() => new(LearningLexicalFailure.TextProviderUnavailable);
}

/// <summary>A narrow native seam for unavailable/bad-boundary tests without changing OS DLL state.</summary>
internal interface IIcuLexicalApi
{
    string ChangeCase(string value, bool upper);
    // Includes every raw break interval, including punctuation and whitespace.
    IReadOnlyList<LearningTextRange> WordRanges(string value, string locale);
    bool HasBinaryProperty(int scalar, int property);
}

/// <summary>
/// Owns ICU calls, pinning UTF-16 only for the lifetime needed by the C API. The breaker retains
/// that pointer across calls; it must close in finally before the fixed buffer can be released.
/// Casing uses a required-length preflight and root locale, rather than a process/user locale.
/// </summary>
internal sealed partial class NativeIcuLexicalApi : IIcuLexicalApi
{
    public static NativeIcuLexicalApi Instance { get; } = new();
    private NativeIcuLexicalApi() { }
    public bool HasBinaryProperty(int scalar, int property) => u_hasBinaryProperty(scalar, property) != 0;

    public string ChangeCase(string value, bool upper)
    {
        // Swift 6.2 String.lowercased/uppercased concatenate each scalar's full mapping, rather
        // than mapping the whole string contextually. ICU over a whole word would turn final Σ
        // into ς, changing the Mac dictionary key. A single scalar retains expansions such as İ/ß.
        var result = new StringBuilder(value.Length);
        foreach (var scalar in value.EnumerateRunes())
        {
            if (scalar.IsAscii)
            {
                int code = scalar.Value;
                if (upper && code is >= 'a' and <= 'z') code -= 32;
                if (!upper && code is >= 'A' and <= 'Z') code += 32;
                result.Append((char)code);
            }
            else result.Append(ChangeScalarCase(scalar.ToString(), upper));
        }
        return result.ToString();
    }

    private static unsafe string ChangeScalarCase(string value, bool upper)
    {
        fixed (char* source = value)
        {
            int status = 0;
            int required = upper ? u_strToUpper(null, 0, source, value.Length, "", &status)
                : u_strToLower(null, 0, source, value.Length, "", &status);
            // U_BUFFER_OVERFLOW_ERROR is expected during preflight. Unicode full mappings have
            // at most three scalars per input scalar. Each output scalar can occupy two UTF-16
            // code units; this conservative code-unit bound also covers supplementary mappings.
            if ((status > 0 && status != 15) || required < 0 || required >= int.MaxValue
                || required > 6L * value.Length + 8) throw Unavailable();
            if (required == 0) return string.Empty;
            var buffer = new char[required + 1];
            fixed (char* destination = buffer)
            {
                status = 0;
                int written = upper ? u_strToUpper(destination, buffer.Length, source, value.Length, "", &status)
                    : u_strToLower(destination, buffer.Length, source, value.Length, "", &status);
                if (status > 0 || written != required) throw Unavailable();
                return new string(buffer, 0, written);
            }
        }
    }

    public unsafe IReadOnlyList<LearningTextRange> WordRanges(string value, string locale)
    {
        fixed (char* source = value)
        {
            int status = 0;
            var iterator = ubrk_open(1, locale, source, value.Length, &status); // UBRK_WORD
            if (iterator == IntPtr.Zero) throw Unavailable();
            try
            {
                if (status > 0 || ubrk_first(iterator) != 0) throw Unavailable();
                var ranges = new List<LearningTextRange>();
                int previous = 0;
                while (true)
                {
                    int end = ubrk_next(iterator);
                    if (end == -1) break; // UBRK_DONE
                    if (end <= previous || end > value.Length) throw Unavailable();
                    ranges.Add(new LearningTextRange(previous, end - previous));
                    previous = end;
                }
                if (previous != value.Length) throw Unavailable();
                return ranges.AsReadOnly();
            }
            finally { ubrk_close(iterator); }
        }
    }
    private static LearningLexicalException Unavailable() => new(LearningLexicalFailure.TextProviderUnavailable);

    [LibraryImport("icu.dll", EntryPoint = "u_strToLower", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int u_strToLower(char* destination, int capacity, char* source, int length, string locale, int* status);
    [LibraryImport("icu.dll", EntryPoint = "u_strToUpper", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial int u_strToUpper(char* destination, int capacity, char* source, int length, string locale, int* status);
    [LibraryImport("icu.dll", EntryPoint = "u_hasBinaryProperty")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial sbyte u_hasBinaryProperty(int scalar, int property);
    [LibraryImport("icu.dll", EntryPoint = "ubrk_open", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static unsafe partial IntPtr ubrk_open(int type, string locale, char* source, int length, int* status);
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
