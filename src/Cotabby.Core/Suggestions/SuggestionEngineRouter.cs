namespace Cotabby.Core.Suggestions;

/// <summary>
/// Sends each request to the engine the user selected. A failing endpoint never
/// falls back to the local model, because the user chose where their text goes.
/// </summary>
public sealed class SuggestionEngineRouter : ISuggestionEngine, ILocalSpeculativeSuggestionEngine
{
    private readonly ISuggestionEngine _local;
    private readonly ISuggestionEngine _endpoint;
    private volatile bool _useEndpoint;

    public SuggestionEngineRouter(ISuggestionEngine local, ISuggestionEngine endpoint)
    {
        _local = local;
        _endpoint = endpoint;
    }

    public bool UseEndpoint
    {
        get => _useEndpoint;
        set => _useEndpoint = value;
    }

    public bool IsReady => Active.IsReady;

    public bool SpacingIsExact => Active.SpacingIsExact;

    public Task<string> GenerateAsync(SuggestionRequest request, CancellationToken ct) =>
        Active.GenerateAsync(request, ct);

    public Task<string> GenerateAsync(SuggestionRequest request, Action<string>? onPartial, CancellationToken ct) =>
        Active.GenerateAsync(request, onPartial, ct);

    public Task<string> GenerateLocalSpeculationAsync(SuggestionRequest request, CancellationToken ct) =>
        _useEndpoint ? Task.FromResult("") : _local.GenerateAsync(request, ct);
    public bool CanSpeculateLocally => !_useEndpoint;

    private ISuggestionEngine Active => _useEndpoint ? _endpoint : _local;
}
