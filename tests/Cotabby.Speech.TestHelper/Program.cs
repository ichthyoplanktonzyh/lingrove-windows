using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

// Test-only executable: real binary pipes exercise process lifetime without loading a model.
// The normal/wait/sequence/truncation effects mirror the pinned Mac Python fixture. Additional
// protocol mutations are explicit test effects; this executable is never bundled with the app.
if (args is ["--descendant", var descendantRoot])
{
    File.WriteAllText(Path.Combine(descendantRoot, "descendant"), Environment.ProcessId.ToString());
    Thread.Sleep(30_000); return;
}
Console.InputEncoding = Encoding.UTF8;
var arguments = args.Chunk(2).ToDictionary(pair => pair[0], pair => pair[1]);
string root = Path.GetDirectoryName(arguments["--model"])!;
File.AppendAllText(Path.Combine(root, "starts"), Environment.ProcessId + "\n");
File.WriteAllText(Path.Combine(root, "arguments"), JsonSerializer.Serialize(arguments));
using var output = Console.OpenStandardOutput();
void Send(object header, byte[]? payload = null)
{
    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(header); Span<byte> length = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(length, encoded.Length); output.Write(length); output.Write(encoded);
    if (payload is not null) output.Write(payload); output.Flush();
}
Send(new { @event = "ready", protocol_version = 2 });
while (Console.ReadLine() is { } line)
{
    using var document = JsonDocument.Parse(line); var request = document.RootElement;
    string text = request.GetProperty("text").GetString()!, id = request.GetProperty("id").GetString()!;
    File.AppendAllText(Path.Combine(root, "parameters"), line + "\n");
    File.AppendAllText(Path.Combine(root, "requests"), text + "\n");
    if (text is "wait" or "ignore-term") Thread.Sleep(10_000);
    if (text == "exit") return;
    if (text == "descendant")
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--descendant"); start.ArgumentList.Add(root);
        using var descendant = System.Diagnostics.Process.Start(start);
    }
    int rate = text == "bad-rate" ? 7999 : 24000;
    byte[] pcm = new byte[9600];
    if (text == "nan") BinaryPrimitives.WriteInt32LittleEndian(pcm, BitConverter.SingleToInt32Bits(float.NaN));
    Send(new { @event = "chunk", id = text == "wrong-id" ? Guid.NewGuid().ToString().ToUpperInvariant() : id,
        sequence = 0, sample_rate = rate, samples = 2400, payload_bytes = 9600, format = "f32le", channels = 1 }, pcm);
    if (text == "chunk-then-wait") Thread.Sleep(10_000);
    if (text == "wrong-sequence")
        Send(new { @event = "chunk", id, sequence = 0, sample_rate = 24000, samples = 2400, payload_bytes = 9600, format = "f32le", channels = 1 }, pcm);
    if (text == "truncated") { output.Write(new byte[] { 0, 0, 0, 200, (byte)'{' }); output.Flush(); return; }
    Send(new { @event = "result", id, ok = text != "not-ok", samples = text == "wrong-total" ? 2401 : 2400, sample_rate = 24000 });
}
File.WriteAllText(Path.Combine(root, "eof"), "graceful");
