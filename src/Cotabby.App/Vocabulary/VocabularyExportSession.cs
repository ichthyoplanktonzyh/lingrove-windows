using System.Windows.Threading;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Vocabulary;

/// <summary>
/// One export window owns this dispatcher-bound state, adapting the Mac sheet's session. It borrows
/// the app Store; preparation/save run in its archive. Generation IDs and a captured key reject late
/// previews after scope/options changes or close. A prepared package identity gates a save-panel
/// callback, so an old preview can never silently save a newly selected scope. No picker lives here.
/// </summary>
internal sealed class VocabularyExportSession
{
    internal readonly record struct Key(VocabularyExportScope Scope, VocabularyExportConfiguration Configuration);
    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyList<VocabularySelection>, VocabularyExportConfiguration, CancellationToken, Task<VocabularyExportPackage>> _prepare;
    private readonly Func<VocabularyExportPackage, string, CancellationToken, Task> _write;
    private VocabularyExportScope _scope;
    private VocabularyExportConfiguration _configuration = new();
    private Guid _generation = Guid.NewGuid();
    private Key? _preparedKey;
    internal VocabularyExportRequest Request { get; }
    internal VocabularyExportScope Scope { get { Verify(); return _scope; } set { Verify(); _scope = value; Notify(); } }
    internal VocabularyExportConfiguration Configuration { get { Verify(); return _configuration; } set { Verify(); _configuration = value; Notify(); } }
    internal VocabularyExportPackage? Package { get; private set; }
    internal bool Loading { get; private set; }
    internal bool Saving { get; private set; }
    internal string? Message { get; private set; }
    internal bool IsActive { get; private set; } = true;
    internal Key CurrentKey { get { Verify(); return new(_scope, _configuration); } }
    internal bool CanExport { get { Verify(); return IsActive && !Loading && !Saving && Package is not null && _preparedKey == CurrentKey; } }
    internal event Action? Changed;

    internal VocabularyExportSession(Dispatcher dispatcher, VocabularyStore store, VocabularyExportRequest request)
        : this(dispatcher, request, store.PrepareExportAsync, store.WriteExportAsync) { }
    /// <summary>Controlled delegates exercise completion order/errors in tests. Production always
    /// binds both to the single Store; the delegates do not create a parallel archive or runtime.</summary>
    internal VocabularyExportSession(Dispatcher dispatcher, VocabularyExportRequest request,
        Func<IReadOnlyList<VocabularySelection>, VocabularyExportConfiguration, CancellationToken, Task<VocabularyExportPackage>> prepare,
        Func<VocabularyExportPackage, string, CancellationToken, Task> write)
    { dispatcher.VerifyAccess(); _dispatcher = dispatcher; Request = request; _scope = request.InitialScope; _prepare = prepare; _write = write; }

    internal async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        Verify(); if (!IsActive) return;
        Guid id = Guid.NewGuid(); var key = CurrentKey;
        _generation = id; Loading = true; Message = null; Package = null; _preparedKey = null; Notify();
        try
        {
            var prepared = await _prepare(Request.Selections(key.Scope), key.Configuration, cancellationToken);
            Verify(); cancellationToken.ThrowIfCancellationRequested();
            if (!IsActive || _generation != id || CurrentKey != key) return;
            Package = prepared; _preparedKey = key; Notify();
        }
        catch (Exception error)
        {
            Verify();
            if (IsActive && _generation == id && error is not OperationCanceledException) { Message = error.Message; Notify(); }
        }
        finally
        {
            Verify(); if (_generation == id) { Loading = false; Notify(); }
        }
    }
    internal async Task<bool> SaveAsync(VocabularyExportPackage package, string destination, CancellationToken cancellationToken = default)
    {
        Verify();
        if (!CanExport || package.Id != Package?.Id || package.Configuration != Configuration) return false;
        Saving = true; Message = null; Notify();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _write(package, destination, cancellationToken); Verify();
            // The archive has acknowledged publication. Closing or late cancellation cannot
            // turn those already-written bytes into a failed save after its commit point.
            return true;
        }
        catch (Exception error)
        {
            Verify(); if (IsActive && error is not OperationCanceledException) { Message = error.Message; Notify(); }
            return false;
        }
        finally { Verify(); Saving = false; Notify(); }
    }
    internal void Cancel()
    { Verify(); IsActive = false; _generation = Guid.NewGuid(); Package = null; Notify(); }
    private void Verify() => _dispatcher.VerifyAccess();
    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch { /* Presentation cannot fail an acknowledged file save. */ }
    }
}
