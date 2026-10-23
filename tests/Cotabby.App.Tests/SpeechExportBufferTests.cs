using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Speech;
using Cotabby.Inference.Speech;

namespace Cotabby.App.Tests;

/// <summary>Actual request-owned export worker compared with the complete original actor. Only
/// immutable synthetic producer bytes are supplied; no runtime, voice, endpoint or player is used.</summary>
public sealed class SpeechExportBufferTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "speech-buffer-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(35, rows.Length);
        Assert.Equal("07e231693506f2b5a4bb27e9fb65d76fae4232c076c0a9bfda596d93576b8e61", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    private static byte[] Bytes(JsonElement action)
    {
        using var data = new MemoryStream(); if (action.TryGetProperty("prefix", out var prefix)) data.Write(prefix.GetBytesFromBase64());
        if (action.TryGetProperty("bits", out var bits)) foreach (var bit in bits.EnumerateArray())
        { byte[] bytes = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, bit.GetUInt32()); data.Write(bytes); }
        else data.Write(new byte[action.TryGetProperty("zeros", out var zeros) ? zeros.GetInt32() : 0]);
        return data.ToArray();
    }
    private static object? Audio(SpeechExportAudio? value) => value is null ? null : new { bytes = value.ByteCount, format = value.FileExtension, sha256 = SpeechExportPolicy.Digest(value.CopyBytes()) };
    private static object? Field(SpeechExportBuffer owner, string name) => typeof(SpeechExportBuffer).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static object Snapshot(SpeechExportBuffer owner)
    {
        // The native oracle adds a read-only same-file actor extension. Reflection gives the
        // test the corresponding snapshot without adding a diagnostics API to production.
        lock (Field(owner, "_gate")!)
        {
            var data = (MemoryStream)Field(owner, "_data")!;
            return new { dataBytes = data.Length, dataSHA256 = SpeechExportPolicy.Digest(data.GetBuffer().AsSpan(0, (int)data.Length)),
                sampleRate = (int?)Field(owner, "_sampleRate"), encoded = Audio((SpeechExportAudio?)Field(owner, "_encoded")) };
        }
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task CompleteOriginalActorTrace(string id, JsonElement row)
    {
        var buffer = new SpeechExportBuffer(); var trace = new List<object> { new { state = Snapshot(buffer), result = (object?)null } };
        foreach (var action in row.GetProperty("actions").EnumerateArray())
        {
            using var cancellation = new CancellationTokenSource(); if (action.TryGetProperty("cancel", out var cancel) && cancel.GetBoolean()) cancellation.Cancel();
            object? result = null;
            try
            {
                switch (action.GetProperty("op").GetString())
                {
                    case "pcm": await buffer.AppendAsync(new SpeechAudioChunk.Pcm(new(Bytes(action), action.GetProperty("rate").GetInt32())), cancellation.Token); break;
                    case "encoded": await buffer.AppendAsync(new SpeechAudioChunk.Encoded(Bytes(action)), cancellation.Token); break;
                    case "package": result = new { error = (string?)null, audio = Audio(await buffer.PackageAsync(new(Bytes(action), action.GetProperty("rate").GetInt32()), cancellation.Token)) }; break;
                    case "finish": result = new { error = (string?)null, audio = Audio(await buffer.FinishAsync()) }; break;
                    default: throw new InvalidOperationException("Unknown export buffer action");
                }
            }
            catch (OperationCanceledException) { result = new { error = "cancelled", audio = (object?)null }; }
            catch (SpeechExportException error) { result = new { error = error.Failure == SpeechExportFailure.InvalidAudio ? "invalidAudio" : "responseTooLarge", audio = (object?)null }; }
            trace.Add(new { state = Snapshot(buffer), result });
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var actual = JsonSerializer.SerializeToNode(trace);
        Assert.True(JsonNode.DeepEquals(expected, actual), id + "\nExpected: " + expected + "\nActual: " + actual);
    }
    [Fact]
    public async Task ConcurrentAppendAndIndependentPackageDoNotMixRecordingsOrLoseSamples()
    {
        var buffer = new SpeechExportBuffer(); var pcm = new SpeechAudioChunk.Pcm(new(new byte[4096], 24_000));
        var recording = new SystemSpeechRecording(new byte[] { 0, 0, 128, 63 }, 24_000);
        var appends = Enumerable.Range(0, 128).Select(_ => buffer.AppendAsync(pcm)).ToArray();
        var packages = Enumerable.Range(0, 8).Select(_ => buffer.PackageAsync(recording)).ToArray();
        await Task.WhenAll(appends);
        var generated = await Task.WhenAll(packages);
        string packageHash = SpeechExportPolicy.Digest(SpeechExportPolicy.Wav(recording.CopyBytes(), 24_000).CopyBytes());
        Assert.All(generated, audio => Assert.Equal(packageHash, SpeechExportPolicy.Digest(audio.CopyBytes())));
        var exported = await buffer.FinishAsync();
        var expected = SpeechExportPolicy.Wav(new byte[128 * 4096], 24_000);
        Assert.Equal(expected.CopyBytes(), exported.CopyBytes()); Assert.Equal(expected.ByteCount, exported.ByteCount);
    }
}
