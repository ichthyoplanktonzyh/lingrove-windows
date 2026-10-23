using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests;

/// <summary>Same recorded complete Swift PCM admission/ledger cases on every platform. Neither
/// the source oracle nor these tests open an audio device or run a speech generator.</summary>
public sealed class SpeechPCMTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-pcm-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(64, rows.Length);
        Assert.Equal("0b44b96497061d5ee205792fd60f8e6f1f19f8e12ca82bee1859efc485fe68be", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public void CompleteOriginalChunkAndQueueObservations(string id, JsonElement row)
    {
        object actual;
        if (row.GetProperty("kind").GetString() == "chunk")
        {
            byte[] bytes;
            if (row.TryGetProperty("bits", out var bits))
            {
                bytes = new byte[bits.GetArrayLength() * 4]; int offset = 0;
                foreach (var bit in bits.EnumerateArray()) { BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), bit.GetUInt32()); offset += 4; }
            }
            else bytes = row.TryGetProperty("data", out var data) ? data.GetBytesFromBase64() : new byte[row.GetProperty("zeros").GetInt32()];
            try
            {
                var value = new SpeechPCMChunk(bytes, row.GetProperty("sampleRate").GetInt32());
                actual = new { rate = value.SampleRate, samples = value.SampleCount, bytes = value.ByteCount, sha256 = Convert.ToHexStringLower(SHA256.HashData(value.CopyBytes())) };
            }
            catch (SpeechExportException error) { Assert.Equal(SpeechExportFailure.InvalidAudio, error.Failure); actual = new { error = "invalidAudio" }; }
        }
        else
        {
            var value = new SpeechPCMPlaybackState();
            object State() => new { queued = value.QueuedSamples, total = value.TotalSamples, ended = value.InputEnded, finished = value.IsFinished };
            var trace = new List<object> { new { state = State(), result = (object?)null } };
            foreach (var step in row.GetProperty("steps").EnumerateArray())
            {
                object? outcome = null;
                try
                {
                    switch (step.GetProperty("op").GetString())
                    {
                        case "enqueue": outcome = value.Enqueue(step.GetProperty("samples").GetInt64()); break;
                        case "played": value.Played(step.GetProperty("id").GetInt64()); break;
                        case "end": value.EndInput(); break;
                        case "start": outcome = value.CanStart(step.GetProperty("sampleRate").GetInt64(), Rate(step.GetProperty("rate").GetString()!)); break;
                        default: throw new InvalidOperationException("Unknown PCM action");
                    }
                }
                catch (SpeechExportException error) { Assert.Equal(SpeechExportFailure.InvalidAudio, error.Failure); outcome = new { error = "invalidAudio" }; }
                trace.Add(new { state = State(), result = outcome });
            }
            actual = trace;
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var observed = JsonSerializer.SerializeToNode(actual);
        Assert.True(JsonNode.DeepEquals(expected, observed), id + "\nExpected: " + expected + "\nActual: " + observed);
    }
    private static double Rate(string value) => value switch
    { "nan" => double.NaN, "infinity" => double.PositiveInfinity, "-infinity" => double.NegativeInfinity, _ => double.Parse(value, CultureInfo.InvariantCulture) };

    [Fact]
    public void DetachedBytesAndValueEqualityPreserveTheAdmittedSnapshot()
    {
        byte[] bytes = [0, 0, 128, 63]; var chunk = new SpeechPCMChunk(bytes, 24_000); var same = new SpeechPCMChunk(bytes, 24_000); int hash = chunk.GetHashCode();
        bytes[3] = 127; Assert.Equal(new byte[] { 0, 0, 128, 63 }, chunk.CopyBytes());
        var copy = chunk.CopyBytes(); copy[3] = 127; Assert.Equal(same, chunk); Assert.Equal(hash, same.GetHashCode());
        Assert.NotEqual(new SpeechPCMChunk(new byte[4], 24_000), chunk); Assert.NotEqual(new SpeechPCMChunk(chunk.CopyBytes(), 8_000), chunk);
        Assert.NotEqual(new SpeechPCMChunk(new byte[4], 24_000), new SpeechPCMChunk(new byte[] { 0, 0, 0, 128 }, 24_000));
        Assert.False(chunk.Equals(null)); Assert.DoesNotContain("128", chunk.ToString());
    }
    [Fact]
    public void IndependentPlaybackOwnersCannotConsumeEachOthersAcknowledgements()
    {
        var first = new SpeechPCMPlaybackState(); var second = new SpeechPCMPlaybackState();
        long id = first.Enqueue(100); second.Enqueue(200); second.EndInput(); first.Played(id);
        Assert.Equal(200, second.QueuedSamples); Assert.False(second.IsFinished); first.EndInput(); Assert.True(first.IsFinished);
        second.Played(0); Assert.True(second.IsFinished);
    }
}
