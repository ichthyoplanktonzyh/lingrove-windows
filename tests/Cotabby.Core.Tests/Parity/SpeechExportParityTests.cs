using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Speech;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Parity;

public sealed class SpeechExportParityTests
{
    private static readonly JsonElement Corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-export-v1.json"))).RootElement.Clone();
    public static IEnumerable<object[]> Cases() => Corpus.GetProperty("cases").EnumerateArray().Select(r => new object[] { Text(r, "id") });
    [Theory, MemberData(nameof(Cases))]
    public void OriginalExportBytesAndAdmission(string id)
    {
        Assert.Equal(1, Corpus.GetProperty("version").GetInt32()); Assert.Equal(73, Corpus.GetProperty("cases").GetArrayLength());
        Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", Corpus.GetProperty("macReference").GetString());
        Assert.Equal("1970b9f0aeff8650b250d8e920eed9273b57f2ab7c86e5d00f2d0f6a0bb77789", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", Corpus.GetProperty("cases").EnumerateArray().Select(r => Text(r, "id")).Order(StringComparer.Ordinal))))));
        var row = Corpus.GetProperty("cases").EnumerateArray().Single(r => Text(r, "id") == id);
        object result;
        try
        {
            string kind = Text(row, "kind", "encoded");
            if (kind == "filename")
            {
                bool valid = SpeechExportPolicy.ValidFilename(Text(row, "name"));
                Assert.Equal(valid, VocabularyArchiveValidation.IsValidAudioFilename(Text(row, "name"))); result = new { valid };
            }
            else
            {
                var bytes = Data(row);
                var audio = kind switch
                {
                    "identity" => new SpeechExportAudio(bytes, Text(row, "extension", "wav")),
                    "pcm" => SpeechExportPolicy.Wav(bytes, row.TryGetProperty("rate", out var rate) ? rate.GetInt32() : 24000),
                    _ => SpeechExportPolicy.Encoded(bytes),
                };
                var snapshot = new Dictionary<string, object> { ["bytes"] = audio.ByteCount, ["extension"] = audio.FileExtension, ["digest"] = SpeechExportPolicy.Digest(audio.CopyBytes()), ["filename"] = SpeechExportPolicy.Filename(audio) };
                if (audio.ByteCount <= 256) snapshot["hex"] = Convert.ToHexStringLower(audio.CopyBytes());
                result = new { valid = true, audio = snapshot };
            }
        }
        catch (SpeechExportException error) { result = new { valid = false, error = error.Message }; }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(result);
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
    }
    private static string Text(JsonElement row, string key, string fallback = "") => row.TryGetProperty(key, out var value) ? value.GetString()! : fallback;
    private static byte[] Data(JsonElement row)
    {
        if (row.TryGetProperty("bytes", out var data)) return Convert.FromBase64String(data.GetString()!);
        if (row.TryGetProperty("zeroBytes", out var count))
        { var prefix = Convert.FromBase64String(Text(row, "prefix")); var bytes = new byte[prefix.Length + count.GetInt32()]; prefix.CopyTo(bytes, 0); return bytes; }
        if (row.TryGetProperty("bits", out var bits))
        { var bytes = new byte[bits.GetArrayLength() * 4]; int i = 0; foreach (var v in bits.EnumerateArray()) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i++ * 4), v.GetUInt32()); return bytes; }
        if (row.TryGetProperty("samples", out var samples))
        { var bytes = new byte[samples.GetArrayLength() * 4]; int i = 0; foreach (var v in samples.EnumerateArray()) BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i++ * 4), BitConverter.SingleToInt32Bits((float)v.GetDouble())); return bytes; }
        return [];
    }
    [Fact]
    public void AudioOwnsBytesAndContentIdentityAcrossCallerAndReturnedBufferEdits()
    {
        byte[] source = [1, 2, 3]; var audio = new SpeechExportAudio(source, "wav"); string name = SpeechExportPolicy.Filename(audio);
        source[0] = 9; var copy = audio.CopyBytes(); copy[1] = 8;
        Assert.Equal(new byte[] { 1, 2, 3 }, audio.CopyBytes()); Assert.Equal(name, SpeechExportPolicy.Filename(audio)); Assert.True(SpeechExportPolicy.ValidFilename(name));
        Assert.Equal("lingrove-" + Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 })) + ".wav", name);
    }
    [Fact]
    public void PcmOutputHasIndependentCanonicalRiffHeaderAndOriginalClampingScale()
    {
        byte[] input = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(input, BitConverter.SingleToInt32Bits(-1)); BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4), BitConverter.SingleToInt32Bits(1));
        var bytes = SpeechExportPolicy.Wav(input, 8000).CopyBytes();
        Assert.Equal(48, bytes.Length); Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4)); Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(bytes, 8, 8)); Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(20)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(22))); Assert.Equal(8000u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)));
        Assert.Equal(16000u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28))); Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(32)));
        Assert.Equal((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(34))); Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(40)));
        Assert.Equal((short)-32767, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44))); Assert.Equal((short)32767, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(46)));
        Assert.Equal(bytes, SpeechExportPolicy.Encoded(bytes).CopyBytes());
    }
}
