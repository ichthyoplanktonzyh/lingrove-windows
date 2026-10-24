using System.Collections.Frozen;
using System.Text.Json;

namespace Cotabby.Core.Text;

/// <summary>
/// One immutable, embedded observation of the editor's Darwin text boundary. Windows never calls
/// Core Foundation or downloads character data. The offline capture covers every Unicode scalar;
/// its OS/build, framework version, generator digest and Unicode InCB source are recorded with it.
/// This is scoped compatibility data, not a promise that every Foundation search API is identical.
/// </summary>
internal static class FoundationEditorTextData
{
    private static readonly JsonElement Data = Load();
    private static readonly FrozenDictionary<int, string> Folds = Mappings("folds");
    private static readonly FrozenDictionary<int, string> Forward = Mappings("forwardCompatibility");
    private static readonly FrozenDictionary<int, int> Classes = Data.GetProperty("combiningClasses").EnumerateArray()
        .ToFrozenDictionary(row => row[0].GetInt32(), row => row[1].GetInt32());
    private static readonly (int First, int Last)[] Extend = Ranges(Data, "graphemeExtend"), NonBase = Ranges(Data, "nonBase"),
        Letters = Ranges(Data, "letters"), Prepend = Ranges(Data, "prepend");
    private static readonly (int First, int Last)[] IndicConsonants = Indic("Consonant"), IndicLinkers = Indic("Linker"), IndicExtends = Indic("Extend");

    private static JsonElement Load()
    {
        using var stream = typeof(FoundationEditorTextData).Assembly.GetManifestResourceStream("Cotabby.Core.Resources.Foundation.editor-text-data-v1.json")
            ?? throw new InvalidOperationException("Missing embedded editor text data.");
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.GetProperty("version").GetInt32() != 1) throw new InvalidOperationException("Unknown editor text data version.");
        return document.RootElement.Clone();
    }
    private static FrozenDictionary<int, string> Mappings(string key) => Data.GetProperty(key).EnumerateArray()
        .ToFrozenDictionary(row => row[0].GetInt32(), row => row[1].GetString()!);
    private static (int, int)[] Ranges(JsonElement owner, string key) => owner.GetProperty(key).EnumerateArray()
        .Select(row => (row[0].GetInt32(), row[1].GetInt32())).ToArray();
    private static (int, int)[] Indic(string key) => Ranges(Data.GetProperty("indicConjunctBreak"), key);
    private static bool Contains((int First, int Last)[] spans, int value)
    {
        int low = 0, high = spans.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2; var span = spans[middle];
            if (value < span.First) high = middle - 1;
            else if (value > span.Last) low = middle + 1;
            else return true;
        }
        return false;
    }
    internal static bool IsExtend(int value) => Contains(Extend, value);
    internal static bool IsNonBase(int value) => Contains(NonBase, value);
    internal static bool IsLetter(int value) => Contains(Letters, value);
    internal static bool IsPrepend(int value) => Contains(Prepend, value);
    internal static bool IsVirama(int value) => value == 0x034f || Classes.GetValueOrDefault(value) == 9;
    internal static string Fold(int value, string original, bool source) => source && Forward.TryGetValue(value, out var forward)
        ? forward : Folds.GetValueOrDefault(value, original);
    internal static int AsciiLower(int value) => value is >= 'A' and <= 'Z' ? value + ('a' - 'A') : value;
    internal static bool IsAscii(string text) => text.AsSpan().IndexOfAnyInRange('\u0080', '\uffff') < 0;

    /// <summary>Read a scalar without mistaking a temporary UTF-16 cursor inside a pair for an exception.</summary>
    internal static int Scalar(string text, int position, out int width)
    {
        if (position >= text.Length) { width = 0; return -1; }
        if (char.IsHighSurrogate(text[position]) && position + 1 < text.Length && char.IsLowSurrogate(text[position + 1]))
        { width = 2; return char.ConvertToUtf32(text[position], text[position + 1]); }
        width = 1; return text[position];
    }

    /// <summary>
    /// GB9c's consonant/linker boundary is relative to this search window. After a raw match,
    /// the next search can start inside the same conjunct; consulting text before that window
    /// would incorrectly remove the duplicate composed spans retained by the Mac editor.
    /// </summary>
    internal static bool InsideIndicConjunct(string text, int position, int searchStart, CancellationToken cancellationToken)
    {
        if (!Contains(IndicConsonants, Scalar(text, position, out _))) return false;
        bool linker = false; int scanned = 0;
        for (int index = position - 1; index >= searchStart; index--)
        {
            if ((scanned++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (char.IsLowSurrogate(text[index]) && index > searchStart && char.IsHighSurrogate(text[index - 1])) index--;
            int value = Scalar(text, index, out _);
            if (Contains(IndicLinkers, value)) linker = true;
            else if (!Contains(IndicExtends, value)) return linker && Contains(IndicConsonants, value);
        }
        return false;
    }
}
