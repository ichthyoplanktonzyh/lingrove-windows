using Cotabby.Core.Models;

namespace Cotabby.App.Speech;

/// <summary>The speech settings borrow their own lazy model manager, with an independent speech
/// directory/catalog and download lifetime. The controller asks only for discovery and supplies
/// actual selected/resident-file protection. Download/import/delete UI remains the manager's
/// boundary; this seam performs none and does not reuse a learning/completion runtime.
/// Like Mac MainActor, callback invocation and protected-name evaluation require the controller
/// UI context. A background transfer must marshal its directory notification before invoking it;
/// it cannot query protection from an arbitrary worker while preferences/runtime are changing.</summary>
internal interface ISpeechModelManagement
{
    Action? DirectoryChanged { get; set; }
    Func<IReadOnlySet<string>> ProtectedModelFilenames { get; set; }
    IReadOnlyList<RuntimeModelOption> InstalledModelOptions();
}
