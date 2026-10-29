using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cotabby.Core.CLI;
using Cotabby.Win32.CLI;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    Guid id = Guid.NewGuid();
    try
    {
        var parsed = CliArguments.Parse(arguments);
        switch (parsed.Mode)
        {
            case CliMode.Help: Console.WriteLine(Help()); return 0;
            case CliMode.Version: Console.WriteLine("lingrove CLI 3 (protocol 1, batch 1/2, capture 1)"); return 0;
            case CliMode.Schema: Console.WriteLine(CliPublishedSchema.Batch().ToJsonString(new JsonSerializerOptions { WriteIndented = true })); return 0;
            case CliMode.CaptureSchema: Console.WriteLine(CliPublishedSchema.Capture().ToJsonString(new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        string app = parsed.AppPath is not null ? ExpandHome(parsed.AppPath)
            : Environment.GetEnvironmentVariable("LINGROVE_CLI_APP") ?? Path.Combine(AppContext.BaseDirectory, "Cotabby.exe");
        using var selected = CliApplicationSelection.Open(app);
        var request = CliRequest.FromArguments(parsed, CliWire.Now() + parsed.TimeoutSeconds); id = request.Id;
        if (parsed.InputPath is { } input)
        {
            if (input != "-") input = ExpandHome(input);
            byte[] bytes = input == "-" ? await ReadStandardInputAsync() : await CliLocalInput.ReadAsync(input, CliInputContract.MaximumBytes);
            var batch = parsed.CaptureInput ? CliInputContract.DecodeCapture(bytes) : CliInputContract.DecodeBatch(bytes);
            string baseDirectory = input == "-" ? Environment.CurrentDirectory : Path.GetDirectoryName(Path.GetFullPath(input))!;
            batch = await CliLocalInput.AttachImagesAsync(batch, baseDirectory);
            request = request with { Batch = batch.CopyValue() };
        }
        request = request with { ExpiresAt = CliWire.Now() + parsed.TimeoutSeconds };
        Func<CancellationToken, Task>? launch = parsed.Launch ? _ => { if (!IsRunning(selected.Identity)) selected.StartInBackground(); return Task.CompletedTask; } : null;
        var reply = await CliMailboxClient.SendAsync(selected.Identity.MailboxRoot, request, launch);
        await WriteAsync(CliWire.Encode(reply));
        return reply.Ok ? 0 : ExitCode(reply.Error?.Code);
    }
    catch (CliException error) { await WriteAsync(CliWire.Encode(CliWire.Failure(id, error))); return error.ExitCode; }
    catch (Exception) { var error = new CliException(CliErrorCode.Io, "Local CLI operation failed."); await WriteAsync(CliWire.Encode(CliWire.Failure(id, error))); return error.ExitCode; }
}

static bool IsRunning(CliApplicationIdentity selected)
{
    foreach (var process in Process.GetProcessesByName("Cotabby"))
    {
        using (process)
        {
            try
            {
                if (process.MainModule?.FileName is { } file && string.Equals(CliApplicationIdentity.Resolve(file).Executable, selected.Executable, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or CliException or NotSupportedException) { }
        }
    }
    return false;
}
static async Task<byte[]> ReadStandardInputAsync()
{
    using var output = new MemoryStream(); var input = Console.OpenStandardInput(); byte[] buffer = new byte[65536];
    while (true)
    {
        int count = await input.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, CliInputContract.MaximumBytes + 1 - (int)output.Length)));
        if (count == 0) break; output.Write(buffer, 0, count);
        if (output.Length > CliInputContract.MaximumBytes) throw new CliException(CliErrorCode.InvalidInput, "Input exceeds 32 MiB.");
    }
    return output.ToArray();
}
static async Task WriteAsync(byte[] bytes) { var output = Console.OpenStandardOutput(); await output.WriteAsync(bytes); await output.WriteAsync(new byte[] { 10 }); await output.FlushAsync(); }
static int ExitCode(string? code) => code switch { "invalid_input" => 2, "not_found" => 3, "conflict" => 4, "busy" => 5, "unavailable" => 6, "timeout" => 8, _ => 7 };
static string ExpandHome(string path) => path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length == 1 ? "" : path[2..]) : path;

static string Help() => """
Usage: lingrove [--app APP_PATH] [--no-launch] [--timeout SECONDS] COMMAND
  status                                     Storage status and revision
  vocab list [--query TEXT] [--language CODE] [--limit N] [--offset N]
  vocab get ENTRY_UUID                       Portable entry and current revision
  vocab apply (--file PATH | --stdin) [--dry-run]
  vocab capture (--file PATH | --stdin) [--dry-run]
  data list RESOURCE [--query TEXT] [--related UUID] [--status STATUS] [--limit N] [--offset N]
  data get RESOURCE UUID                     Full record and current revision
  schema [capture]                           Batch or capture-task JSON Schema
  --version | --help

Images use explicit local paths, relative to the input file (stdin: current directory).
Writes are atomic. Retry an uncertain write with the SAME idempotencyKey and content.
Resource put/delete use batch version 2 and expectedRevision; UUIDs are caller supplied.
Resource timestamps are Unix seconds. Catalog/graph writes do not implicitly collect material.
Successful requests and errors use JSON on stdout. Exit codes: 0 success, 2 input,
3 missing ID, 4 conflict, 5 busy, 6 app unavailable, 7 IO, 8 timeout.
""";
