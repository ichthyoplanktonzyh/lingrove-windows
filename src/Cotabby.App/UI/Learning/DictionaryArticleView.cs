using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Cotabby.Core.LanguageLearning;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Cotabby.App.UI.Learning;

/// <summary>Untrusted dictionary markup runs in a private, script-disabled WebView. Every
/// request is denied unless it is the current article or an explicitly scoped local resource.</summary>
internal sealed class DictionaryArticleView : Grid, IAsyncDisposable
{
    private sealed record Target(Guid Id, string Document, Func<string, CancellationToken, Task<DictionaryResource?>> Load,
        Func<bool> Permit, CancellationToken Token);
    // Composition control must stay loaded while its native controller is initialized.
    private readonly WebView2CompositionControl _view = new() { Focusable = false };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly HashSet<Task> _work = []; private readonly object _gate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _profile; private CoreWebView2Environment? _environment; private Task? _initialization;
    private Task? _nativePending;
    private readonly Func<Task>? _environmentBarrier, _controllerBarrier;
    // Retired SDK operations remain rooted until they really finish and their private profile is removed.
    private static readonly object CleanupGate = new();
    private static readonly HashSet<Task> CleanupOwners = [];
    internal Task ProfileCleanup { get; private set; } = Task.CompletedTask;
    internal string ProfilePath => _profile;
    private CancellationTokenSource? _targetCancel; private Target? _target; private long _epoch;
    private bool _allowInitial, _disposed; private Task? _dispose;
    private int _resourceRequests, _resourceBytes;
    internal event Action<string>? ReferenceRequested;
    internal string? InitializationFailure { get; private set; }
    internal DictionaryArticleView(Func<Task>? environmentBarrier = null, Func<Task>? controllerBarrier = null)
    {
        _environmentBarrier = environmentBarrier; _controllerBarrier = controllerBarrier;
        _profile = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "lingrove-dictionary-" + Guid.NewGuid().ToString("N")));
        ((UIElement)_view).Opacity = 0;
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary"); Children.Add(_status); Children.Add(_view);
    }
    internal async Task ShowAsync(LearningDictionaryResult.Article article,
        Func<string, CancellationToken, Task<DictionaryResource?>> load, Func<bool> permit, CancellationToken token)
    {
        Dispatcher.VerifyAccess(); if (_disposed) return;
        Retire(); if (article.ResourceLibrary is not { } id || !permit() || token.IsCancellationRequested || Encoding.UTF8.GetByteCount(article.Html) > 8_000_000) return;
        _resourceRequests = _resourceBytes = 0;
        var cancel = CancellationTokenSource.CreateLinkedTokenSource(token); _targetCancel = cancel; long epoch = ++_epoch;
        _status.Text = "Opening local dictionary…"; _status.Visibility = Visibility.Visible; ((UIElement)_view).Opacity = 0;
        try
        {
            _target = new(id, DictionaryHtmlPolicy.Document(article.Html), load, permit, cancel.Token);
            _initialization ??= InitializeAsync(); await _initialization;
            if (!Current(epoch)) return;
            _allowInitial = true; _view.CoreWebView2.Navigate(DictionaryResourceRequestPolicy.ArticleUri(id));
            _status.Visibility = Visibility.Collapsed; ((UIElement)_view).Opacity = 1;
        }
        catch (Exception error)
        {
            InitializationFailure = error.GetType().Name + " 0x" + error.HResult.ToString("X8");
            if (!_disposed && epoch == _epoch && !cancel.IsCancellationRequested)
            { _status.Text = "Rich dictionary display is unavailable. Check that Microsoft Edge WebView2 Runtime is installed, then reopen the dictionary."; _status.Visibility = Visibility.Visible; }
        }
    }
    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(_profile);
        if (_environmentBarrier is not null) await AwaitNativeAsync(_environmentBarrier());
        var registration = new CoreWebView2CustomSchemeRegistration(DictionaryResourceRequestPolicy.Scheme) { HasAuthorityComponent = true };
        var options = new CoreWebView2EnvironmentOptions(customSchemeRegistrations: [registration]) { AreBrowserExtensionsEnabled = false,
            ExclusiveUserDataFolderAccess = true, IsCustomCrashReportingEnabled = true,
            AdditionalBrowserArguments = "--disable-background-networking --disable-component-update --disable-sync --no-first-run" };
        var environmentTask = CoreWebView2Environment.CreateAsync(null, _profile, options);
        await AwaitNativeAsync(environmentTask); _environment = await environmentTask;
        if (_disposed) return;
        if (_controllerBarrier is not null) await AwaitNativeAsync(_controllerBarrier());
        var controller = _environment.CreateCoreWebView2ControllerOptions(); controller.IsInPrivateModeEnabled = true;
        await AwaitNativeAsync(_view.EnsureCoreWebView2Async(_environment, controller));
        var core = _view.CoreWebView2; var settings = core.Settings;
        settings.IsScriptEnabled = false; settings.IsWebMessageEnabled = false; settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDevToolsEnabled = false; settings.AreDefaultContextMenusEnabled = false; settings.IsStatusBarEnabled = false;
        settings.IsPasswordAutosaveEnabled = false; settings.IsGeneralAutofillEnabled = false;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += ResourceRequested;
        core.NavigationStarting += (_, e) =>
        {
            var target = _target;
            if (_allowInitial && target is not null && e.Uri == DictionaryResourceRequestPolicy.ArticleUri(target.Id) && Current(_epoch))
            { _allowInitial = false; return; }
            e.Cancel = true;
            if (e.IsUserInitiated && target is not null && Current(_epoch) && DictionaryLinkPolicy.Reference(e.Uri) is { } query)
                ReferenceRequested?.Invoke(query);
        };
        core.FrameNavigationStarting += (_, e) => e.Cancel = true;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
    }
    private async Task AwaitNativeAsync(Task native)
    {
        _nativePending = native;
        await native.WaitAsync(TimeSpan.FromSeconds(10), _shutdown.Token);
        _nativePending = null;
    }
    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var environment = _environment; if (environment is null) return;
        // A response is installed before any asynchronous work, so errors never fall through to network.
        e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Forbidden", "Cache-Control: no-store\r\n");
        var target = _target; long epoch = _epoch;
        if (target is null || !Current(epoch) || !string.Equals(e.Request.Method, "GET", StringComparison.Ordinal)) return;
        if (e.ResourceContext == CoreWebView2WebResourceContext.Document && e.Request.Uri == DictionaryResourceRequestPolicy.ArticleUri(target.Id))
        {
            e.Response = Response(Encoding.UTF8.GetBytes(target.Document), "text/html; charset=utf-8"); return;
        }
        if (e.ResourceContext is not (CoreWebView2WebResourceContext.Image or CoreWebView2WebResourceContext.Stylesheet or CoreWebView2WebResourceContext.Font)) return;
        string? path = DictionaryResourceRequestPolicy.ResourcePath(e.Request.Uri, target.Id); if (path is null) return;
        if (++_resourceRequests > 128) return;
        var deferral = e.GetDeferral(); Track(CompleteResourceAsync(e, deferral, target, path, epoch));
    }
    private async Task CompleteResourceAsync(CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral, Target target, string path, long epoch)
    {
        try
        {
            var resource = await target.Load(path, target.Token);
            if (Current(epoch) && resource is not null && resource.Data.Length <= 8_000_000
                && resource.Data.Length <= 32_000_000 - _resourceBytes && AllowedMime(resource.MimeType, e.ResourceContext))
            { _resourceBytes += resource.Data.Length; e.Response = Response(resource.Data, resource.MimeType); }
        }
        catch { /* Keep the preinstalled deny response. */ }
        finally { deferral.Complete(); }
    }
    private static bool AllowedMime(string mime, CoreWebView2WebResourceContext context) => context switch
    {
        CoreWebView2WebResourceContext.Image => mime is "image/png" or "image/jpeg" or "image/gif" or "image/webp",
        CoreWebView2WebResourceContext.Stylesheet => mime == "text/css",
        CoreWebView2WebResourceContext.Font => mime is "font/woff" or "font/woff2" or "font/ttf" or "font/otf",
        _ => false,
    };
    private CoreWebView2WebResourceResponse Response(byte[] bytes, string mime) => _environment!.CreateWebResourceResponse(
        new MemoryStream(bytes, false), 200, "OK", "Content-Type: " + mime + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n");
    private bool Current(long epoch)
    {
        try { return !_disposed && epoch == _epoch && _target is { } target && !target.Token.IsCancellationRequested && target.Permit(); }
        catch { return false; }
    }
    internal void Retire()
    {
        Dispatcher.VerifyAccess(); ++_epoch; _target = null; _allowInitial = false; ((UIElement)_view).Opacity = 0;
        _view.CoreWebView2?.Stop();
        var cancel = _targetCancel; _targetCancel = null;
        if (cancel is not null) Track(CancelAsync(cancel));
    }
    private static async Task CancelAsync(CancellationTokenSource owner) { try { await owner.CancelAsync(); } finally { owner.Dispose(); } }
    private void Track(Task task)
    {
        lock (_gate) _work.Add(task);
        _ = task.ContinueWith(t => { _ = t.Exception; lock (_gate) _work.Remove(t); }, TaskScheduler.Default);
    }
    public ValueTask DisposeAsync() { Dispatcher.VerifyAccess(); return new(_dispose ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        _disposed = true; Retire(); _shutdown.Cancel();
        while (true) { Task[] tasks; lock (_gate) tasks = _work.ToArray(); if (tasks.Length == 0) break; try { await Task.WhenAll(tasks); } catch { } }
        _view.Dispose();
        if (_initialization is { } initialization) try { await initialization; } catch { }
        _shutdown.Dispose();
        var pending = _nativePending;
        ProfileCleanup = CleanupProfileAsync(pending, _profile);
        lock (CleanupGate) CleanupOwners.Add(ProfileCleanup);
        _ = ProfileCleanup.ContinueWith(t => { _ = t.Exception; lock (CleanupGate) CleanupOwners.Remove(t); }, TaskScheduler.Default);
    }
    private static async Task CleanupProfileAsync(Task? pending, string profile)
    {
        if (pending is not null) try { await pending.ConfigureAwait(false); } catch { }
        // The path is generated by this control under the system temp directory; never accept a user path.
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (profile.StartsWith(parent + "lingrove-dictionary-", StringComparison.OrdinalIgnoreCase))
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                try { if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true); return; }
                catch (IOException) when (attempt < 39) { await Task.Delay(250).ConfigureAwait(false); }
                catch (UnauthorizedAccessException) when (attempt < 39) { await Task.Delay(250).ConfigureAwait(false); }
            }
        }
    }
}
