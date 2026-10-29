using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using Cotabby.Core.Text;

namespace Cotabby.Core.LanguageLearning.MDict;

// Adapted from MDictReader, MDictHeaderParser, MDictKeyBlocks, MDictKeyIndex and MDictRecords,
// Lingrove 3b53402 / Easydict b4eced7. Copyright © 2026 izual and Easydict contributors.
// GPL-3.0; see resources/ThirdParty/MDictReader. The caller owns the seekable stream.
public sealed class MDictFileReader
{
    private const int MaxMetadata = 16 * 1024 * 1024, MaxBlocks = 200_000, MaxEntries = 20_000_000;
    private const int MaxCompressed = 33 * 1024 * 1024, MaxRecord = 8 * 1024 * 1024, MaxResult = 16 * 1024 * 1024;
    private readonly Stream _stream; private readonly Func<string, string> _lowercase; private readonly Encoding _encoding;
    private readonly bool _v2, _caseSensitive; private readonly int _terminator;
    private readonly List<KeyBlock> _keys = []; private readonly List<RecordBlock> _records = [];
    private readonly Dictionary<int, KeyEntry[]> _keyCache = []; private readonly List<int> _keyOrder = [];
    private readonly Dictionary<int, byte[]> _recordCache = []; private readonly List<int> _recordOrder = []; private long _recordCacheBytes;
    private sealed record KeyBlock(string First, string Last, long Position, int Compressed, int Expanded, int Start, int Count);
    private sealed record KeyEntry(string Word, ulong Offset, int Index);
    private sealed record RecordBlock(long Position, int Compressed, int Expanded, ulong Start);
    public string Title { get; }
    public bool IsHtml { get; }
    public int EntryCount { get; }
    private ulong TotalRecordBytes => _records.Count == 0 ? 0 : checked(_records[^1].Start + (ulong)_records[^1].Expanded);

    public MDictFileReader(Stream stream, Func<string, string> lowercase, CancellationToken cancellationToken = default)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream)); _lowercase = lowercase ?? throw new ArgumentNullException(nameof(lowercase));
        if (!stream.CanRead || !stream.CanSeek || stream.Length <= 0 || stream.Length > 8L * 1024 * 1024 * 1024) throw Invalid();
        cancellationToken.ThrowIfCancellationRequested(); long cursor = 0;
        int headerLength = ToInt(ReadNumber(ref cursor, 4));
        if (headerLength > 2 * 1024 * 1024 || headerLength % 2 != 0) throw Invalid();
        string header = new UnicodeEncoding(false, false, true).GetString(Read(ref cursor, headerLength)); Skip(ref cursor, 4);
        string? Attribute(string name) { var m = Regex.Match(header, Regex.Escape(name) + "=\"([^\"]*)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); return m.Success ? m.Groups[1].Value : null; }
        string version = Attribute("GeneratedByEngineVersion") ?? "2.0";
        if (!double.TryParse(version, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) number = 2;
        if (!double.IsFinite(number) || number < 1 || number >= 3) throw Invalid(); _v2 = number >= 2;
        Title = Attribute("Title") ?? ""; IsHtml = (Attribute("Format") ?? "Html").Contains("html", StringComparison.OrdinalIgnoreCase);
        _caseSensitive = string.Equals(Attribute("KeyCaseSensitive") ?? "No", "yes", StringComparison.OrdinalIgnoreCase);
        string name = (Attribute("Encoding") ?? "utf-8").Trim().ToLowerInvariant();
        _encoding = name switch
        {
            "utf-16" or "utf-16le" => new UnicodeEncoding(false, false, true), "utf-16be" => new UnicodeEncoding(true, false, true),
            "gb2312" or "gb18030" or "gbk" => CodePagesEncodingProvider.Instance.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) ?? throw Invalid(),
            "big5" => CodePagesEncodingProvider.Instance.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) ?? throw Invalid(),
            "" when header.Contains("<Library_Data", StringComparison.Ordinal) => new UnicodeEncoding(false, false, true), _ => new UTF8Encoding(false, true),
        };
        _terminator = _encoding.CodePage is 1200 or 1201 ? 2 : 1;
        string encryption = (Attribute("Encrypted") ?? "0").Trim().ToLowerInvariant();
        int encrypted = int.TryParse(encryption, out var parsed) ? parsed : encryption is "" or "false" or "no" or "none" ? 0 : 1;
        if ((encrypted & 1) != 0 || (encrypted & ~3) != 0) throw Invalid();
        int width = _v2 ? 8 : 4;
        int keyBlocks = ToInt(ReadNumber(ref cursor, width)), entries = ToInt(ReadNumber(ref cursor, width));
        if (keyBlocks > MaxBlocks || entries > MaxEntries) throw Invalid();
        int infoExpanded = _v2 ? ToInt(ReadNumber(ref cursor, width)) : 0;
        int infoLength = ToInt(ReadNumber(ref cursor, width)); _ = ReadNumber(ref cursor, width);
        if (infoLength > MaxMetadata || infoExpanded > MaxMetadata) throw Invalid(); if (_v2) Skip(ref cursor, 4);
        var infoRaw = Read(ref cursor, infoLength);
        if (_v2 && infoLength > 0) { if ((encrypted & 2) != 0) infoRaw = MDictCompression.DecryptKeyInfo(infoRaw); infoRaw = MDictCompression.Expand(infoRaw, infoExpanded); }
        int ip = 0, entryStart = 0; long keyPosition = cursor; string previousFirst = "", previousLast = "";
        if (keyBlocks > infoRaw.Length / (3 * width + 2)) throw Invalid();
        for (int i = 0; i < keyBlocks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); int count = ToInt(ReadNumber(infoRaw, ref ip, width));
            string first = Boundary(infoRaw, ref ip), last = Boundary(infoRaw, ref ip);
            int compressed = ToInt(ReadNumber(infoRaw, ref ip, width)), expanded = ToInt(ReadNumber(infoRaw, ref ip, width));
            if (compressed > MaxCompressed || expanded > MDictCompression.MaximumBlock || count > MaxEntries - entryStart) throw Invalid();
            string normalizedFirst = Normalize(first), normalizedLast = Normalize(last); var comparer = CanonicalStringComparer.Instance;
            if (comparer.Compare(normalizedFirst, normalizedLast) > 0 || comparer.Compare(previousFirst, normalizedFirst) > 0 || comparer.Compare(previousLast, normalizedLast) > 0) throw Invalid();
            Ensure(keyPosition, compressed); _keys.Add(new(normalizedFirst, normalizedLast, keyPosition, compressed, expanded, entryStart, count));
            keyPosition = checked(keyPosition + compressed); entryStart += count; previousFirst = normalizedFirst; previousLast = normalizedLast;
        }
        if (entryStart != entries) throw Invalid(); EntryCount = entries; cursor = keyPosition;
        int recordBlocks = ToInt(ReadNumber(ref cursor, width)); _ = ReadNumber(ref cursor, width); _ = ReadNumber(ref cursor, width); _ = ReadNumber(ref cursor, width);
        if (recordBlocks > MaxBlocks || recordBlocks > (stream.Length - cursor) / (2 * width)) throw Invalid();
        var sizes = new (int Compressed, int Expanded)[recordBlocks];
        for (int i = 0; i < sizes.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested(); int compressed = ToInt(ReadNumber(ref cursor, width)), expanded = ToInt(ReadNumber(ref cursor, width));
            if (compressed > MaxCompressed || expanded > MDictCompression.MaximumBlock) throw Invalid(); sizes[i] = (compressed, expanded);
        }
        ulong expandedPosition = 0;
        foreach (var size in sizes)
        {
            Ensure(cursor, size.Compressed); _records.Add(new(cursor, size.Compressed, size.Expanded, expandedPosition));
            cursor = checked(cursor + size.Compressed); expandedPosition = checked(expandedPosition + (uint)size.Expanded);
        }
    }

    public IReadOnlyList<string> Definitions(string query, CancellationToken token = default) =>
        Records(query, token).Select(bytes => { try { return _encoding.GetString(bytes).Replace("\0", ""); } catch (DecoderFallbackException) { return null; } })
            .Where(x => x is not null).Cast<string>().ToArray();

    public IReadOnlyList<byte[]> Records(string query, CancellationToken token = default)
    {
        string key = Normalize(query); var comparer = CanonicalStringComparer.Instance; int work = 0; long workBytes = 0;
        var matches = new List<KeyEntry>(); var candidate = new List<int>();
        int lower = 0, upper = _keys.Count;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2;
            if (comparer.Compare(key, _keys[middle].Last) > 0) lower = middle + 1; else upper = middle;
        }
        for (int i = lower; i < _keys.Count; i++)
        {
            var block = _keys[i]; if (comparer.Compare(key, block.First) < 0) break;
            if (comparer.Compare(key, block.Last) <= 0) candidate.Add(i);
            if (comparer.Compare(key, block.Last) < 0) break;
        }
        void Scan(int i)
        {
            token.ThrowIfCancellationRequested(); var block = _keys[i];
            if (++work > 64 || (workBytes += block.Expanded) > 128L * 1024 * 1024) throw Invalid();
            matches.AddRange(KeyEntries(i).Where(entry => comparer.Equals(Normalize(entry.Word), key)));
            if (matches.Count > 16) throw Invalid();
        }
        foreach (int index in candidate) Scan(index);
        if (matches.Count == 0)
            for (int i = 0; i < _keys.Count; i++)
            {
                token.ThrowIfCancellationRequested(); var b = _keys[i];
                if (!candidate.Contains(i) && comparer.Compare(key, b.First) >= 0 && comparer.Compare(key, b.Last) <= 0)
                { Scan(i); if (matches.Count != 0) break; }
            }
        var result = new List<byte[]>(); var seen = new HashSet<(ulong, int)>(); int bytes = 0;
        foreach (var match in matches)
        {
            token.ThrowIfCancellationRequested(); ulong end = NextOffset(match); if (end < match.Offset || end > TotalRecordBytes || end - match.Offset > MaxRecord) throw Invalid();
            int count = (int)(end - match.Offset); if (count == 0 || !seen.Add((match.Offset, count))) continue;
            if (result.Count >= 16 || count > MaxResult - bytes) throw Invalid(); bytes += count; result.Add(ReadRecord(match.Offset, count, token));
        }
        return result;
    }

    private KeyEntry[] KeyEntries(int index)
    {
        if (_keyCache.TryGetValue(index, out var cached)) { Touch(_keyOrder, index); return cached; }
        var b = _keys[index]; long position = b.Position; var bytes = MDictCompression.Expand(Read(ref position, b.Compressed), b.Expanded);
        int pos = 0, width = _v2 ? 8 : 4; if (b.Count > 65536 || b.Count > bytes.Length / (width + _terminator)) throw Invalid();
        var entries = new List<KeyEntry>(b.Count);
        while (pos < bytes.Length)
        {
            ulong offset = ReadNumber(bytes, ref pos, width); int end = pos;
            while (end + _terminator <= bytes.Length && (_terminator == 1 ? bytes[end] != 0 : bytes[end] != 0 || bytes[end + 1] != 0)) end += _terminator;
            if (end + _terminator > bytes.Length) throw Invalid();
            string word = _encoding.GetString(bytes, pos, end - pos); pos = end + _terminator;
            entries.Add(new(word, offset, b.Start + entries.Count));
        }
        if (entries.Count != b.Count) throw Invalid(); var result = entries.ToArray(); _keyCache[index] = result; Touch(_keyOrder, index);
        while (_keyOrder.Count > 4) { _keyCache.Remove(_keyOrder[0]); _keyOrder.RemoveAt(0); } return result;
    }
    private ulong NextOffset(KeyEntry entry)
    {
        if (entry.Index + 1 >= EntryCount) return TotalRecordBytes;
        int target = entry.Index + 1, lower = 0, upper = _keys.Count;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2; var block = _keys[middle];
            if (target < block.Start) upper = middle;
            else if (target >= block.Start + block.Count) lower = middle + 1;
            else return KeyEntries(middle)[target - block.Start].Offset;
        }
        throw Invalid();
    }
    private byte[] ReadRecord(ulong offset, int length, CancellationToken token)
    {
        var result = new byte[length]; int written = 0;
        while (written < length)
        {
            token.ThrowIfCancellationRequested(); int index = RecordIndex(offset);
            if (index < 0) throw Invalid(); var block = _records[index]; var data = RecordBytes(index); int start = checked((int)(offset - block.Start));
            int count = Math.Min(length - written, data.Length - start); if (count <= 0) throw Invalid();
            data.AsSpan(start, count).CopyTo(result.AsSpan(written)); written += count; offset += (uint)count;
        }
        return result;
    }
    private int RecordIndex(ulong offset)
    {
        int lower = 0, upper = _records.Count;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2; var block = _records[middle];
            if (offset < block.Start) upper = middle;
            else if (offset >= block.Start + (uint)block.Expanded) lower = middle + 1;
            else return middle;
        }
        return -1;
    }
    private byte[] RecordBytes(int index)
    {
        if (_recordCache.TryGetValue(index, out var cached)) { Touch(_recordOrder, index); return cached; }
        var b = _records[index]; long position = b.Position; var data = MDictCompression.Expand(Read(ref position, b.Compressed), b.Expanded);
        _recordCache[index] = data; _recordCacheBytes += data.Length; Touch(_recordOrder, index);
        while (_recordOrder.Count > 8 || _recordCacheBytes > 50L * 1024 * 1024) { int old = _recordOrder[0]; _recordOrder.RemoveAt(0); _recordCacheBytes -= _recordCache[old].Length; _recordCache.Remove(old); }
        return data;
    }
    private string Boundary(byte[] bytes, ref int offset)
    {
        int count = (int)ReadNumber(bytes, ref offset, _v2 ? 2 : 1); int length = checked((count + (_v2 ? 1 : 0)) * _terminator);
        if (length > bytes.Length - offset || length < (_v2 ? _terminator : 0)) throw Invalid();
        string text = _encoding.GetString(bytes, offset, length - (_v2 ? _terminator : 0)); offset += length; return text;
    }
    private string Normalize(string value) => _caseSensitive ? value : _lowercase(value);
    private static void Touch(List<int> list, int index) { list.Remove(index); list.Add(index); }
    private static int ToInt(ulong value) => value <= int.MaxValue ? (int)value : throw Invalid();
    private void Ensure(long offset, int count) { if (offset < 0 || count < 0 || offset > _stream.Length || count > _stream.Length - offset) throw Invalid(); }
    private void Skip(ref long offset, int count) { Ensure(offset, count); offset += count; }
    private byte[] Read(ref long offset, int count) { Ensure(offset, count); _stream.Position = offset; var data = new byte[count]; _stream.ReadExactly(data); offset += count; return data; }
    private ulong ReadNumber(ref long offset, int width) { var data = Read(ref offset, width); int p = 0; return ReadNumber(data, ref p, width); }
    private static ulong ReadNumber(byte[] bytes, ref int offset, int width)
    {
        if (offset < 0 || width > bytes.Length - offset) throw Invalid(); var span = bytes.AsSpan(offset, width); offset += width;
        return width switch { 1 => span[0], 2 => BinaryPrimitives.ReadUInt16BigEndian(span), 4 => BinaryPrimitives.ReadUInt32BigEndian(span), 8 => BinaryPrimitives.ReadUInt64BigEndian(span), _ => throw Invalid() };
    }
    private static InvalidDataException Invalid() => new("Invalid or unsupported dictionary file.");
}
