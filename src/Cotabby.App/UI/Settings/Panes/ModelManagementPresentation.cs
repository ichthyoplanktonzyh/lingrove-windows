using System.Globalization;
using System.Text;
using System.Windows.Controls;
using Cotabby.Core.Models;

namespace Cotabby.App.UI.Settings.Panes;

/// <summary>Bounds untrusted settings text only for presentation; stored model/source identity is untouched.</summary>
internal static class ModelDisplayText
{
    private const int MaximumScalars = 256;
    internal static string Name(string filename) => LocalModelCatalog.Descriptor(filename)?.Name ?? Safe(filename);
    internal static string Unavailable(string filename, LocalModelTask task) => Name(filename) +
        (LocalModelTaskPolicy.Supports(filename, task) ? " (file unavailable)" : " · not compatible with this task");
    internal static string Safe(string? value, int maximumScalars = MaximumScalars, int maximumUtf16 = 512)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var result = new StringBuilder(); int index = 0, count = 0;
        while (index < value.Length && count < maximumScalars && result.Length < maximumUtf16)
        {
            var status = Rune.DecodeFromUtf16(value.AsSpan(index), out var rune, out int consumed);
            if (status != System.Buffers.OperationStatus.Done) { result.Append('\uFFFD'); index++; count++; continue; }
            if (result.Length + rune.Utf16SequenceLength > maximumUtf16) break;
            index += consumed; count++;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                result.Append(' ');
            else result.Append(rune.ToString());
        }
        if (index < value.Length && result.Length < maximumUtf16) result.Append('…');
        return result.ToString();
    }
}

/// <summary>One view's async listing epoch. Both results and failures publish only while current.</summary>
internal sealed class ModelScanLifetime
{
    private int _version;
    private bool _closed;
    internal async Task RunAsync<T>(Func<Task<T>> read, Action<T> publish, Action failure, CancellationToken cancellation)
    {
        int version = ++_version;
        try
        {
            var value = await read();
            if (!_closed && version == _version && !cancellation.IsCancellationRequested) publish(value);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (!_closed && version == _version && !cancellation.IsCancellationRequested) failure();
        }
    }
    internal void Close() { _closed = true; _version++; }
}

/// <summary>Transfer state survives disposal of a manager view; only current labels receive updates.</summary>
internal sealed class ModelTransferStatusStore
{
    internal const string Starting = "Starting download…";
    private readonly Dictionary<string, string> _statuses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBlock> _labels = new(StringComparer.OrdinalIgnoreCase);
    internal void BeginView() => _labels.Clear();
    internal void Bind(string filename, TextBlock label, string fallback)
    {
        _labels[filename] = label;
        label.Text = _statuses.GetValueOrDefault(filename) ?? fallback;
    }
    internal void Set(string filename, string text)
    {
        _statuses[filename] = text;
        if (_labels.TryGetValue(filename, out var label)) label.Text = text;
    }
    internal void Start(string filename) => Set(filename, Starting);
}
