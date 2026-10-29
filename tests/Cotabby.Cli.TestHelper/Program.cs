using System.Text.Json;
Console.Error.WriteLine("ready");
var input = await Console.In.ReadToEndAsync();
Console.Out.WriteLine(JsonSerializer.Serialize(new { args, input, app = Environment.GetEnvironmentVariable("LINGROVE_CLI_APP") }));
return 17;
