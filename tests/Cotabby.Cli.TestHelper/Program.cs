using System.Text.Json;
if (args.Length == 2 && args[0] == "lease")
{
    using var lease = new FileStream(args[1], FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
    lease.Lock(0, 1); Console.Error.WriteLine("ready");
    _ = await Console.In.ReadToEndAsync(); lease.Unlock(0, 1); return 0;
}
Console.Error.WriteLine("ready");
var input = await Console.In.ReadToEndAsync();
Console.Out.WriteLine(JsonSerializer.Serialize(new { args, input, app = Environment.GetEnvironmentVariable("LINGROVE_CLI_APP") }));
return 17;
