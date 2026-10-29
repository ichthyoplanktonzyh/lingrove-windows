using System.Globalization;

namespace Cotabby.Core.CLI;

/// <summary>3b command grammar. Parsing is pure: only the client reads files, launches, or transmits.</summary>
public enum CliMode { Help, Version, Schema, CaptureSchema, Request }
public enum CliCommand { Status, List, Get, Apply, ResourceList, ResourceGet }
public enum CliErrorCode { InvalidInput, NotFound, Conflict, Busy, Unavailable, Io, Timeout }
public sealed class CliException(CliErrorCode code, string message) : Exception(message)
{
    public CliErrorCode Code { get; } = code;
    public int ExitCode => Code switch { CliErrorCode.InvalidInput => 2, CliErrorCode.NotFound => 3, CliErrorCode.Conflict => 4, CliErrorCode.Busy => 5, CliErrorCode.Unavailable => 6, CliErrorCode.Io => 7, CliErrorCode.Timeout => 8, _ => 7 };
}
public sealed record CliArguments(CliMode Mode, CliCommand Command = CliCommand.Status, string? AppPath = null,
    bool Launch = true, double TimeoutSeconds = 30, string? InputPath = null, bool CaptureInput = false, bool DryRun = false,
    string? Query = null, string? Language = null, int? Limit = null, int? Offset = null, Guid? EntryId = null,
    string? Resource = null, Guid? ResourceId = null, Guid? RelatedId = null, string? ClaimStatus = null)
{
    public static IReadOnlyList<string> Resources { get; } = Array.AsReadOnly(new[] { "entries", "senses", "sources", "occurrences", "interpretations", "collections", "nodes", "patterns", "mappings", "relations", "cues", "cueTargets", "frameworks", "categories", "categoryParents", "categoryMemberships", "categoryOverrides" });
    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args.SequenceEqual(new[] { "--help" }) || args.SequenceEqual(new[] { "help" })) return new(CliMode.Help);
        if (args.SequenceEqual(new[] { "--version" })) return new(CliMode.Version);
        if (args.SequenceEqual(new[] { "schema" })) return new(CliMode.Schema);
        if (args.SequenceEqual(new[] { "schema", "capture" })) return new(CliMode.CaptureSchema);
        var flags = new HashSet<string>(StringComparer.Ordinal); var options = new Dictionary<string, string>(StringComparer.Ordinal); var words = new List<string>();
        var boolean = new HashSet<string>(["--dry-run", "--no-launch", "--stdin"], StringComparer.Ordinal);
        var valued = new HashSet<string>(["--app", "--timeout", "--query", "--language", "--limit", "--offset", "--file", "--related", "--status"], StringComparer.Ordinal);
        for (int i = 0; i < args.Count; i++)
        {
            string token = args[i];
            if (boolean.Contains(token)) { if (!flags.Add(token)) throw Invalid("Duplicate flag: " + token); }
            else if (valued.Contains(token))
            {
                if (options.ContainsKey(token) || i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw Invalid("Missing value or duplicate option: " + token);
                options.Add(token, args[++i]);
            }
            else if (token.StartsWith("--", StringComparison.Ordinal)) throw Invalid("Unknown option: " + token);
            else words.Add(token);
        }
        string? Take(string key) => options.Remove(key, out var value) ? value : null;
        int? Integer(string key) { string? text = Take(key); if (text is null) return null; if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)) throw Invalid("Invalid integer: " + key); return value; }
        string? app = Take("--app"), timeoutValue = Take("--timeout"); double timeout = 30;
        if (timeoutValue is not null && (!double.TryParse(timeoutValue, NumberStyles.Float, CultureInfo.InvariantCulture, out timeout) || !double.IsFinite(timeout) || timeout is < 1 or > 120)) throw Invalid("Timeout must be 1–120 seconds.");
        var parsed = new CliArguments(CliMode.Request, AppPath: app, Launch: !flags.Contains("--no-launch"), TimeoutSeconds: timeout);
        if (words.SequenceEqual(new[] { "status" })) parsed = parsed with { Command = CliCommand.Status };
        else if (words.SequenceEqual(new[] { "vocab", "list" })) parsed = parsed with { Command = CliCommand.List, Query = Take("--query"), Language = Take("--language"), Limit = Integer("--limit"), Offset = Integer("--offset") };
        else if (words.Count == 2 && words[0] == "vocab" && words[1] is "apply" or "capture")
        {
            string? input = Take("--file"); if (flags.Contains("--stdin")) { if (input is not null) throw Invalid("Choose --file or --stdin."); input = "-"; }
            if (input is null) throw Invalid("Apply requires --file PATH or --stdin.");
            parsed = parsed with { Command = CliCommand.Apply, InputPath = input, CaptureInput = words[1] == "capture", DryRun = flags.Contains("--dry-run") };
        }
        else if (words.Count is 3 or 4 && words[0] == "data" && Resources.Contains(words[2], StringComparer.Ordinal))
        {
            if (words[1] == "list" && words.Count == 3)
            {
                string? related = Take("--related"); Guid? id = null; if (related is not null) { if (!Guid.TryParseExact(related, "D", out var value)) throw Invalid("--related requires a UUID."); id = value; }
                parsed = parsed with { Command = CliCommand.ResourceList, Resource = words[2], Query = Take("--query"), Limit = Integer("--limit"), Offset = Integer("--offset"), ClaimStatus = Take("--status"), RelatedId = id };
            }
            else if (words[1] == "get" && words.Count == 4 && Guid.TryParseExact(words[3], "D", out var id)) parsed = parsed with { Command = CliCommand.ResourceGet, Resource = words[2], ResourceId = id };
            else throw Invalid("Invalid data command.");
        }
        else if (words.Count == 3 && words[0] == "vocab" && words[1] == "get" && Guid.TryParseExact(words[2], "D", out var entry)) parsed = parsed with { Command = CliCommand.Get, EntryId = entry };
        else throw Invalid("Unknown command. Run 'lingrove --help'.");
        if (options.Count != 0 || parsed.Command != CliCommand.Apply && (flags.Contains("--dry-run") || flags.Contains("--stdin"))) throw Invalid("An option is not valid for this command.");
        return parsed;
    }
    private static CliException Invalid(string message) => new(CliErrorCode.InvalidInput, message);
}
