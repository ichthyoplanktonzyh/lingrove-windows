using System.Collections.Frozen;
using System.ComponentModel;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

internal enum VocabularyGraphCommand { ZoomIn, ZoomOut, Fit, Locate }

/// <summary>Window-owned UI state adapted from Mac GraphPresentation. It owns one layout worker,
/// transient expansion/focus/camera and explicit command identity. It borrows preferences only;
/// frames flow directly to the future canvas rather than publishing a whole notebook per tick.</summary>
internal sealed class VocabularyGraphPresentation : INotifyPropertyChanged
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly IVocabularyGraphPreferences _preferences;
    private VocabularyGraphSettings _settings;
    private FrozenSet<Guid> _expanded = FrozenSet<Guid>.Empty;
    private Guid? _focusedEntry;
    private ulong _epoch;
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlySet<Guid> Expanded => _expanded;
    public Guid? FocusedEntryId => _focusedEntry;
    public long CommandSerial { get; private set; }
    public VocabularyGraphCommand Command { get; private set; } = VocabularyGraphCommand.Fit;
    public VocabularyGraphLayoutWorker Worker { get; } = new();
    private VocabularyGraphCamera _camera = new();
    private bool _hasCamera, _fitsInitialLayout = true;
    public VocabularyGraphCamera Camera { get => _camera; set { VerifyAccess(); _camera = value; } }
    public bool HasCamera { get => _hasCamera; set { VerifyAccess(); _hasCamera = value; } }
    public bool FitsInitialLayout { get => _fitsInitialLayout; set { VerifyAccess(); _fitsInitialLayout = value; } }
    public VocabularyGraphSettings Settings
    {
        get => _settings;
        set
        {
            VerifyAccess(); _settings = value.Normalized;
            _preferences.WriteSettings(VocabularyGraphSettingsCodec.EncodeNormalized(_settings)); Changed(nameof(Settings));
        }
    }
    public VocabularyGraphPresentation(IVocabularyGraphPreferences preferences)
    {
        _preferences = preferences;
        var data = preferences.ReadSettings();
        _settings = (data is null ? null : VocabularyGraphSettingsCodec.Decode(data))?.Normalized ?? new();
    }
    public void Toggle(Guid entry)
    {
        VerifyAccess(); var expanded = _expanded.ToHashSet(); var previousFocus = _focusedEntry;
        if (expanded.Remove(entry)) { if (_focusedEntry == entry) _focusedEntry = null; }
        else { expanded.Add(entry); _focusedEntry = entry; }
        _expanded = expanded.ToFrozenSet(); Changed(nameof(Expanded));
        if (previousFocus != _focusedEntry) Changed(nameof(FocusedEntryId));
    }
    public void Reveal(VocabularySelection? selection)
    {
        VerifyAccess(); if (selection is not { } target) return;
        if (!_expanded.Contains(target.EntryId)) { _expanded = _expanded.Append(target.EntryId).ToFrozenSet(); Changed(nameof(Expanded)); }
        if (_focusedEntry != target.EntryId) { _focusedEntry = target.EntryId; Changed(nameof(FocusedEntryId)); }
    }
    public void RevealMeaning(Guid id, IReadOnlySet<Guid>? ancestors = null)
    {
        VerifyAccess(); _expanded = _expanded.Concat(ancestors ?? new HashSet<Guid>()).Append(id).ToFrozenSet(); _focusedEntry = id; Changed(nameof(Expanded)); Changed(nameof(FocusedEntryId));
    }
    public void RevealCategory(Guid id, IReadOnlySet<Guid>? ancestors = null) => RevealMeaning(id, ancestors);
    public void ClearFocus() { VerifyAccess(); _focusedEntry = null; Changed(nameof(FocusedEntryId)); }
    public void Collapse() { VerifyAccess(); _expanded = FrozenSet<Guid>.Empty; _focusedEntry = null; Changed(nameof(Expanded)); Changed(nameof(FocusedEntryId)); }
    public void Send(VocabularyGraphCommand command)
    { VerifyAccess(); Command = command; CommandSerial = unchecked(CommandSerial + 1); Changed(nameof(CommandSerial)); }
    public ulong NextEpoch() { VerifyAccess(); _epoch = unchecked(_epoch + 1); return _epoch; }
    private void VerifyAccess()
    { if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("Graph presentation belongs to its window thread."); }
    private void Changed(string property) => PropertyChanged?.Invoke(this, new(property));
}
