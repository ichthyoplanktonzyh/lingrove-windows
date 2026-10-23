using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cotabby.Core.Review;
using Cotabby.Core.Speech;

namespace Cotabby.Core.Tests;

/// <summary>Complete original decoder observations run against the actual C# reader. The driver
/// only assembles synthetic wire bytes and invokes Append/Next/Finish; no framing rule is mocked.</summary>
public sealed class SpeechAudioFrameDecoderTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "speech-frame-v1.json")));
        Assert.Equal(1, d.RootElement.GetProperty("version").GetInt32()); Assert.Equal("43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f", d.RootElement.GetProperty("macReference").GetString());
        var rows = d.RootElement.GetProperty("cases").EnumerateArray().ToArray(); Assert.Equal(122, rows.Length);
        Assert.Equal("55f380d9291a369a45fd0e801f7bb377fb446798af15379ceed3d2a897af3f6c", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", rows.Select(r => r.GetProperty("id").GetString()!).Order(StringComparer.Ordinal))))));
        return rows.Select(r => new object[] { r.GetProperty("id").GetString()!, r.Clone() }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public void CompleteOriginalByteStreamTrace(string id, JsonElement row)
    {
        using var wire = new MemoryStream();
        if (row.TryGetProperty("raw", out var raw)) wire.Write(raw.GetBytesFromBase64());
        if (row.TryGetProperty("wires", out var specs)) foreach (var spec in specs.EnumerateArray())
        {
            var header = spec.TryGetProperty("headerBytes", out var headerBytes) ? headerBytes.GetBytesFromBase64() : Encoding.UTF8.GetBytes(spec.GetProperty("header").GetString()!);
            byte[] size = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(size, (uint)header.Length); wire.Write(size); wire.Write(header);
            wire.Write(spec.TryGetProperty("payload", out var payload) ? payload.GetBytesFromBase64() : new byte[spec.TryGetProperty("zeros", out var zeros) ? zeros.GetInt32() : 0]);
        }
        var bytes = wire.ToArray(); var decoder = new SpeechAudioFrameDecoder(); var trace = new List<object?>();
        foreach (var step in row.GetProperty("steps").EnumerateArray())
        {
            object? result = null;
            try
            {
                switch (step.GetProperty("op").GetString())
                {
                    case "append":
                        if (step.TryGetProperty("zeros", out var zeros)) decoder.Append(new byte[zeros.GetInt32()]);
                        else decoder.Append(bytes.AsSpan(step.TryGetProperty("offset", out var offset) ? offset.GetInt32() : 0, step.TryGetProperty("count", out var count) ? count.GetInt32() : bytes.Length));
                        break;
                    case "next":
                        if (decoder.Next() is { } frame)
                        {
                            var h = frame.Header;
                            result = new { header = new { @event = h.Event, protocolVersion = h.ProtocolVersion, id = h.Id, sequence = h.Sequence,
                                sampleRate = h.SampleRate, samples = h.Samples, payloadBytes = h.PayloadBytes, ok = h.Ok, format = h.Format, channels = h.Channels },
                                bytes = frame.PayloadByteCount, sha256 = Convert.ToHexStringLower(SHA256.HashData(frame.CopyPayload())) };
                        }
                        break;
                    case "finish": decoder.Finish(); break;
                    default: throw new InvalidOperationException("Unknown decoder action");
                }
            }
            catch (SpeechExportException error) { Assert.Equal(SpeechExportFailure.InvalidAudio, error.Failure); result = new { error = "invalidAudio" }; }
            catch (JsonException) { result = new { error = "decode" }; }
            trace.Add(result);
        }
        var expected = JsonNode.Parse(row.GetProperty("expected").GetRawText()); var observed = JsonSerializer.SerializeToNode(trace);
        Assert.True(JsonNode.DeepEquals(expected, observed), id + "\nExpected: " + expected + "\nActual: " + observed);
    }
    private static byte[] Wire(ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload = default)
    {
        var wire = new byte[4 + header.Length + payload.Length]; BinaryPrimitives.WriteUInt32BigEndian(wire, (uint)header.Length);
        header.CopyTo(wire.AsSpan(4)); payload.CopyTo(wire.AsSpan(4 + header.Length)); return wire;
    }
    [Fact]
    public void FramePayloadOwnsItsSnapshotAcrossInputMutationAndLaterBufferConsumption()
    {
        byte[] payload = [0, 0, 128, 63]; var input = Wire("{\"event\":\"chunk\",\"payload_bytes\":4}"u8, payload); var decoder = new SpeechAudioFrameDecoder();
        decoder.Append(input); Array.Fill(input, (byte)0); var first = Assert.IsType<SpeechAudioFrame>(decoder.Next());
        decoder.Append(Wire("{\"event\":\"chunk\",\"payload_bytes\":4}"u8, new byte[4])); _ = decoder.Next(); decoder.Finish();
        Assert.Equal(payload, first.CopyPayload()); var copy = first.CopyPayload(); Array.Fill(copy, (byte)255); Assert.Equal(payload, first.CopyPayload());
    }
    [Fact]
    public void LazyHeaderStringDecodingDoesNotBroadenExistingEagerWireAdmissionOrExposeHeaderData()
    {
        byte[] ignored = "{\"event\":\"ready\",\"ignored\":\"?\"}"u8.ToArray(); ignored[Array.IndexOf(ignored, (byte)'?')] = 255;
        var decoder = new SpeechAudioFrameDecoder(); decoder.Append(Wire(ignored)); Assert.Equal("ready", decoder.Next()!.Header.Event); decoder.Finish();
        byte[] eager = "{\"result\":null,\"error\":null,\"ignored\":\"?\"}"u8.ToArray(); eager[Array.IndexOf(eager, (byte)'?')] = 255;
        Assert.Throws<DecoderFallbackException>(() => AnkiWirePolicy.Response(eager));
        decoder.Append(Wire("{\"event\":\"fixture-private-header\",\"id\":42}"u8));
        var error = Assert.Throws<JsonException>(() => decoder.Next()); Assert.Equal("Invalid speech frame header.", error.Message); Assert.Null(error.InnerException);
        Assert.DoesNotContain("fixture-private-header", error.ToString());
    }
}
