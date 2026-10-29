namespace Cotabby.Core.Suggestions;

/// <summary>Applies only effective writing changes to the shared coordinator. Local selection
/// identity is independent of endpoint identity; inactive profiles cannot retire active work.</summary>
public sealed class SuggestionSettingsApplication(SuggestionCoordinator coordinator, CompletionModelAdmission local)
{
    private SuggestionSettings? _previous;
    private object? _endpointIdentity, _localIdentity;

    public readonly record struct Result(bool CoordinatorChanged, bool EndpointChanged, bool LocalChanged)
    {
        // Apply performs exactly one local withdrawal when LocalChanged is true. Callers use the
        // resulting epoch for compare-and-select, detecting intervening power notifications.
        public long SelectionEpoch(long priorEpoch) => priorEpoch + (LocalChanged ? 1 : 0);
    }

    public Result Apply(SuggestionSettings settings, object? endpointIdentity, object? localIdentity)
    {
        ArgumentNullException.ThrowIfNull(settings);
        bool wasLocal = _previous is { UsesEndpoint: false, Enabled: true };
        bool isLocal = !settings.UsesEndpoint && settings.Enabled;
        bool localChanged = _previous is null || wasLocal != isLocal || !Equals(_localIdentity, localIdentity);
        bool endpointChanged = _previous is null || !Equals(_endpointIdentity, endpointIdentity)
            || _previous.UsesEndpoint != settings.UsesEndpoint;
        bool effectiveChanged = _previous is null || !Equivalent(_previous, settings);
        if (localChanged && (wasLocal || isLocal)) local.Withdraw();
        bool coordinatorChanged = effectiveChanged || settings.UsesEndpoint && endpointChanged || localChanged && (wasLocal || isLocal);
        if (coordinatorChanged) coordinator.Settings = settings;
        _previous = settings; _endpointIdentity = endpointIdentity; _localIdentity = localIdentity;
        return new(coordinatorChanged, endpointChanged, localChanged && (wasLocal || isLocal));
    }

    private static bool Equivalent(SuggestionSettings left, SuggestionSettings right)
    {
        if (!left.BlockedApps.SetEquals(right.BlockedApps)
            || !left.ShortcutOverrides.SequenceEqual(right.ShortcutOverrides)
            || !left.SpellingDictionaries.SequenceEqual(right.SpellingDictionaries)
            || !left.Languages.SequenceEqual(right.Languages)) return false;
        // Record equality covers all scalar/value properties while the four collection properties
        // are compared by content above, rather than by freshly allocated collection identity.
        return (left with { BlockedApps = right.BlockedApps, ShortcutOverrides = right.ShortcutOverrides,
            SpellingDictionaries = right.SpellingDictionaries, Languages = right.Languages }) == right;
    }
}
