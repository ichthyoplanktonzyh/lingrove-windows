using Cotabby.App.Runtime;

namespace Cotabby.App.Learning;

internal sealed partial class LearningPreviewController
{
    private PerformanceHistoryCollector? _performanceCollector;
    /// <summary>The shared app history is borrowed; the coordinator sends only detached metadata.</summary>
    internal void AttachPerformanceHistory(PerformanceHistoryCollector collector)
    {
        _dispatcher.VerifyAccess();
        if (_performanceCollector is not null && !ReferenceEquals(_performanceCollector, collector))
            throw new InvalidOperationException("Performance history is already attached.");
        if (_performanceCollector is not null) return;
        _performanceCollector = collector;
        _coordinator.RequestObserved += collector.Observe;
    }
    private void DetachPerformanceHistory()
    {
        if (_performanceCollector is { } collector) _coordinator.RequestObserved -= collector.Observe;
        _performanceCollector = null;
    }
}
