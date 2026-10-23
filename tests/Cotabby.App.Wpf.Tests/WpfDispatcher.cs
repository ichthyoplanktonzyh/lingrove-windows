using System.Windows;
using System.Windows.Threading;

namespace Cotabby.App.Wpf.Tests;

/// <summary>
/// Owns one STA dispatcher and a plain WPF Application for this test collection. Views load their
/// real compiled XAML/resources on that thread; the product App and AppHost are never constructed.
/// A single lifetime matters because WPF permits only one Application per test process. Each test
/// closes its own windows before the fixture shuts down the dispatcher. No editor, clipboard,
/// global input, model, network provider or artifact file participates in these view tests.
/// </summary>
public sealed class WpfDispatcher : IDisposable
{
    private readonly Thread _thread;
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.SetResult(Dispatcher.CurrentDispatcher);
                application.Run();
            }
            catch (Exception exception) { ready.TrySetException(exception); }
        }) { IsBackground = true, Name = "Learning view test STA" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _dispatcher = ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }

    public Task Run(Action test) => _dispatcher.InvokeAsync(test, DispatcherPriority.Normal).Task
        .WaitAsync(TimeSpan.FromSeconds(30));

    public void Dispose()
    {
        _dispatcher.InvokeAsync(() => Application.Current.Shutdown()).Task
            .WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        if (!_thread.Join(TimeSpan.FromSeconds(10))) throw new TimeoutException("WPF test dispatcher did not shut down.");
    }
}

// Avoid other WPF collections running against a second Application or the same window dispatcher.
[CollectionDefinition("WPF views", DisableParallelization = true)]
public sealed class WpfViewCollection : ICollectionFixture<WpfDispatcher>;
