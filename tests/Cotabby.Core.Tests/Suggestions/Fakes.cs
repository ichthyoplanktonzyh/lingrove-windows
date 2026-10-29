using System.Collections.Concurrent;
using Cotabby.Core.Context;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Insertion;
using Cotabby.Core.Overlay;
using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests.Suggestions;

internal static class Fields
{
    public static FocusedField Text(string text, string process = "notepad", int pid = 100, bool singleLine = false) => new()
    {
        ProcessId = pid,
        ProcessName = process,
        Text = text,
        CaretOffset = text.Length,
        CaretRect = new ScreenRect(10, 10, 1, 20),
        FieldRect = new ScreenRect(0, 0, 400, 200),
        IsSingleLine = singleLine,
        IsSecure = false,
        FieldId = "editor",
        HasReliableSelection = true,
        IsTextWindowTruncated = false,
        FocusChangeSequence = 1,
        Composition = InputCompositionState.Clear,
        CompositionSequence = 1,
    };
}

internal static class Keys
{
    public const int VkEscape = 0x1B;

    public static KeyboardEvent Char(char c) => new()
    {
        Kind = KeyKind.Character,
        Character = c,
        IsKeyDown = true,
        VirtualKey = char.ToUpperInvariant(c),
    };

    public static KeyboardEvent Tab(bool repeat = false) => Special(KeyKind.Tab, KeyBinding.VkTab, repeat);

    public static KeyboardEvent Special(KeyKind kind, int virtualKey = 0, bool repeat = false) => new()
    {
        Kind = kind,
        Character = '\0',
        IsKeyDown = true,
        VirtualKey = virtualKey,
        IsRepeat = repeat,
    };
}

internal sealed class FakeHook : IKeyboardHook
{
    public event EventHandler<KeyEventArgs>? KeyEvent;

    /// <summary>Raises the event and reports whether a handler swallowed the key.</summary>
    public bool Press(KeyboardEvent ev)
    {
        var args = new KeyEventArgs(ev);
        KeyEvent?.Invoke(this, args);
        return args.Suppress;
    }

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}

/// <summary>A text field that behaves like a host app: typed and inserted text lands in it.</summary>
internal sealed class FakeFocus : IFocusTracker, ITextInserter
{
    private readonly object _gate = new();
    private FocusedField? _field;
    private long _focusChangeSequence;
    private int _refreshesOnPump;

    public event EventHandler<FocusedField?>? FocusChanged;

    public List<string> Insertions { get; } = [];
    // Overlay publication can beat the background host insertion. Read acknowledgement under
    // the same gate as ApplyEdit instead of enumerating a List while another thread appends.
    public string[] InsertionSnapshot { get { lock (_gate) return Insertions.ToArray(); } }
    public List<FocusedField> InsertTargets { get; } = [];
    public List<FocusedField> ReplaceTargets { get; } = [];
    public Func<FocusedField?>? RefreshOverride { get; set; }
    public Action<FocusedField?>? AfterRefresh { get; set; }
    public Func<FocusedField, string, CancellationToken, Task<bool>>? InsertOverride { get; set; }
    public Func<FocusedField, int, string, CancellationToken, Task<bool>>? ReplaceOverride { get; set; }
    public int RefreshesOnPump => Volatile.Read(ref _refreshesOnPump);

    public FocusedField? Current
    {
        get { lock (_gate) return _field; }
    }

    public FocusedField? Refresh()
    {
        if (SynchronizationContext.Current is PumpContext) Interlocked.Increment(ref _refreshesOnPump);
        var field = RefreshOverride is { } refresh ? refresh() : Current;
        AfterRefresh?.Invoke(field);
        return field;
    }

    public void Focus(FocusedField? field)
    {
        FocusedField? focused;
        lock (_gate)
        {
            _focusChangeSequence++;
            focused = field is null ? null : field with { FocusChangeSequence = _focusChangeSequence };
            _field = focused;
        }
        FocusChanged?.Invoke(this, focused);
    }

    public void Append(string text)
    {
        lock (_gate)
        {
            var updated = _field!.Text + text;
            _field = _field with { Text = updated, CaretOffset = updated.Length };
        }
    }

    /// <summary>Changes the field without a focus event, as a mouse click inside the same app does.</summary>
    public void Replace(FocusedField field)
    {
        lock (_gate) _field = field;
    }

    public void PublishComposition(InputCompositionState state, long sequence)
    {
        FocusedField field;
        lock (_gate) { field = _field! with { Composition = state, CompositionSequence = sequence }; _field = field; }
        FocusChanged?.Invoke(this, field);
    }

    public void Move(ScreenRect caret)
    {
        lock (_gate) _field = _field! with { CaretRect = caret };
    }

    public async Task<bool> InsertAsync(FocusedField target, string text, CancellationToken ct)
    {
        InsertTargets.Add(target);
        if (ct.IsCancellationRequested) return false;
        if (InsertOverride is { } insert) return await insert(target, text, ct).ConfigureAwait(false);

        lock (_gate)
        {
            if (!InsertionTargetPolicy.IsCurrentTarget(target, _field)) return false;
            Insertions.Add(text);
            ApplyEdit(target, deleteCount: 0, text);
            return true;
        }
    }

    public async Task<bool> ReplaceAsync(FocusedField target, int deleteCount, string text, CancellationToken ct)
    {
        ReplaceTargets.Add(target);
        if (ct.IsCancellationRequested) return false;
        if (ReplaceOverride is { } replace)
        {
            return await replace(target, deleteCount, text, ct).ConfigureAwait(false);
        }

        lock (_gate)
        {
            if (!InsertionTargetPolicy.IsCurrentTarget(target, _field)) return false;
            ApplyEdit(target, deleteCount, text);
            return true;
        }
    }

    private void ApplyEdit(FocusedField target, int deleteCount, string text)
    {
        int start = target.CaretOffset - deleteCount;
        string updated = target.Text[..start] + text + target.Text[target.CaretOffset..];
        _field = target with { Text = updated, CaretOffset = start + text.Length };
    }

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}

internal sealed class ScriptedEngine : ISuggestionEngine
{
    public Func<SuggestionRequest, string> Completion { get; set; } = _ => " world again";
    public Func<SuggestionRequest, CancellationToken, Task<string>>? GenerateOverride { get; set; }
    public ConcurrentQueue<SuggestionRequest> Requests { get; } = new();
    public bool IsReady { get; set; } = true;
    public bool SpacingIsExact => true;

    public async Task<string> GenerateAsync(SuggestionRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        if (GenerateOverride is { } generate)
        {
            return await generate(request, ct).ConfigureAwait(false);
        }

        await Task.Yield();
        return Completion(request);
    }
}

/// <summary>A clipboard whose contents only change when the test copies something.</summary>
internal sealed class FakeClipboard : IClipboardReader
{
    private readonly object _gate = new();
    private string? _text;
    private uint _sequenceNumber = 7;
    private int _reads;

    public FakeClipboard(string? text = null) => _text = text;

    public int Reads
    {
        get { lock (_gate) return _reads; }
    }

    public uint SequenceNumber
    {
        get { lock (_gate) return _sequenceNumber; }
    }

    public string? ReadText()
    {
        lock (_gate)
        {
            _reads++;
            return _text;
        }
    }

    public void Copy(string text)
    {
        lock (_gate)
        {
            _text = text;
            _sequenceNumber++;
        }
    }
}

internal sealed class RecordingOverlay : IOverlayPresenter
{
    private readonly object _gate = new();
    private string _text = "";
    private readonly Dictionary<string, List<TaskCompletionSource<bool>>> _waiters = new(StringComparer.Ordinal);

    public string Text
    {
        get { lock (_gate) return _text; }
    }

    public OverlayStyle Style { get; private set; }

    public bool ShowsCorrection => Style == OverlayStyle.Correction;

    public ScreenRect Anchor { get; private set; }

    public Task WaitForTextAsync(string text)
    {
        TaskCompletionSource<bool> waiter;
        lock (_gate)
        {
            if (_text == text) return Task.CompletedTask;
            waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_waiters.TryGetValue(text, out var waiters))
            {
                waiters = [];
                _waiters.Add(text, waiters);
            }
            waiters.Add(waiter);
        }

        return waiter.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public void Show(string text, ScreenRect anchor, OverlayStyle style = OverlayStyle.Inline)
    {
        Style = style;
        Anchor = anchor;
        Update(text);
    }

    public void Update(string text)
    {
        List<TaskCompletionSource<bool>>? completed;
        lock (_gate)
        {
            _text = text;
            _waiters.Remove(text, out completed);
        }
        if (completed is not null)
        {
            foreach (var waiter in completed) waiter.TrySetResult(true);
        }
    }

    public void Hide() => Update("");
}

/// <summary>Runs admitted callbacks in order on one owned thread. Closing drains and joins it;
/// best-effort late watcher posts are dropped like a dispatcher that has begun shutdown.</summary>
internal sealed class PumpContext : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly object _lifetime = new();
    private readonly Thread _thread;
    private bool _stopping;
    internal bool IsAlive => _thread.IsAlive;
    internal bool IsStopping { get { lock (_lifetime) return _stopping; } }

    public PumpContext()
    {
        _thread = new Thread(() =>
        {
            SetSynchronizationContext(this);
            foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
        })
        { IsBackground = true, Name = "Coordinator test UI pump" };
        _thread.Start();
    }

    public Task InvokeAsync(Action action)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool posted = TryPost(_ =>
        {
            try
            {
                action();
                completed.TrySetResult(true);
            }
            catch (Exception ex)
            {
                completed.TrySetException(ex);
            }
        }, null);
        return posted ? completed.Task.WaitAsync(TimeSpan.FromSeconds(5)) : Task.FromException(new ObjectDisposedException(nameof(PumpContext)));
    }

    public void BlockUntil(Task release, TaskCompletionSource<bool> entered) => Post(_ =>
    {
        entered.TrySetResult(true);
        release.GetAwaiter().GetResult();
    }, null);

    public override void Post(SendOrPostCallback callback, object? state) => TryPost(callback, state);
    private bool TryPost(SendOrPostCallback callback, object? state)
    {
        lock (_lifetime)
        {
            // A disposed coordinator's best-effort watcher may wake after its UI closes.
            // Match dispatcher shutdown: drop such raw posts instead of throwing on a worker.
            if (_stopping) return false;
            _queue.Add((callback, state)); return true;
        }
    }
    public void Dispose()
    {
        if (Thread.CurrentThread == _thread) throw new InvalidOperationException("The test owner must join its pump from outside the UI thread.");
        lock (_lifetime)
        {
            if (_stopping) return;
            _stopping = true; _queue.CompleteAdding();
        }
        // Already-admitted callbacks drain in order; no producer can add after completion.
        if (!_thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("Coordinator test pump did not drain.");
        _queue.Dispose();
    }
}
