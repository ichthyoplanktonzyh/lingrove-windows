using System.Diagnostics;
using System.Text;

try
{
    string encoded = Environment.GetEnvironmentVariable("LINGROVE_CLI_APP_B64") ?? throw new InvalidOperationException();
    if (encoded.Length > 65536) throw new InvalidOperationException();
    string app = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
    app = Path.GetFullPath(app);
    if (!string.Equals(Path.GetFileName(app), "Cotabby.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
    using var selection = PinnedCommand.Open(app);
    selection.Verify();
    var start = new ProcessStartInfo(selection.CliPath) { UseShellExecute = false };
    start.Environment["LINGROVE_CLI_APP"] = app;
    foreach (string argument in args) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException();
    selection.Verify();
    await process.WaitForExitAsync();
    return process.ExitCode;
}
catch (Exception)
{
    Console.Out.WriteLine("{\"version\":1,\"requestID\":\"00000000-0000-0000-0000-000000000000\",\"ok\":false,\"error\":{\"code\":\"unavailable\",\"message\":\"Cannot start the selected command-line tool.\"}}");
    return 6;
}
