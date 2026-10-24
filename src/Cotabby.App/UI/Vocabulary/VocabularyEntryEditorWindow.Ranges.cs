using System.Windows;
using System.Windows.Threading;
using Cotabby.Core.Vocabulary;
using Cotabby.Core.Text;

namespace Cotabby.App.UI.Vocabulary;

/// <summary>
/// Windows presentation adapter for the Mac editor's synchronous range callbacks. Unicode search
/// can be expensive for long repeated prefixes, so the modal runs the unchanged pure policy away
/// from its dispatcher. Each source event captures its own text/expression/form; newer work retires
/// old work, and publication replaces only ranges on still-matching usages. Save waits for the
/// current result. The modal owns cancellation and joins all workers before disposing its lifetime.
/// </summary>
public partial class VocabularyEntryEditorWindow
{
    private sealed record RangeInput(Guid Id, string Text, string Expression, string Form);
    private readonly Dictionary<Guid, RangeInput> _rangeInputs = [];
    private CancellationTokenSource? _rangeCancellation;
    private Task _rangeDrain = Task.CompletedTask;
    private long _rangeRevision;
    private bool _reanchoring, _rangeFailed;
    internal Task LastReanchor { get; private set; } = Task.CompletedTask;

    private void QueueRanges(IEnumerable<VocabularyExample> examples)
    {
        if (_retired || _saving) return;
        foreach (var example in examples) _rangeInputs[example.Id] = new(example.Id, example.Text, _draft.Expression, _draft.DictionaryForm);
        RestartRanges();
    }
    private void PruneRangeTargets()
    {
        if (_rangeInputs.Count == 0) return;
        var live = (Sense?.Examples ?? []).Select(example => example.Id).ToHashSet(); bool changed = false;
        foreach (var id in _rangeInputs.Keys.Where(id => !live.Contains(id)).ToArray()) changed |= _rangeInputs.Remove(id);
        if (changed) RestartRanges();
    }
    private void RestartRanges()
    {
        _rangeCancellation?.Cancel(); long revision = ++_rangeRevision;
        if (_rangeFailed) FailureText.Visibility = Visibility.Collapsed;
        _rangeFailed = false; _reanchoring = _rangeInputs.Count > 0;
        if (!_reanchoring) { _rangeCancellation = null; LastReanchor = Task.CompletedTask; UpdateAdmission(); return; }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _rangeCancellation = cancellation; var inputs = _rangeInputs.Values.ToArray();
        LastReanchor = ReanchorAsync(inputs, revision, cancellation);
        // Only task receipts are retained by the join; stale workers cannot publish into the draft.
        // Completed WhenAll tasks release their antecedents, so finished snapshots need not live
        // for the rest of a long editing session.
        _rangeDrain = Task.WhenAll(_rangeDrain, LastReanchor); UpdateAdmission();
    }
    private async Task ReanchorAsync(RangeInput[] inputs, long revision, CancellationTokenSource cancellation)
    {
        try
        {
            var ranges = await Task.Run(() => inputs.ToDictionary(input => input.Id,
                input => VocabularyPolicy.ReconciledRanges(input.Text, input.Expression, input.Form, cancellation.Token)), cancellation.Token).ConfigureAwait(false);
            await PublishRangesAsync(revision, cancellation.Token, () =>
            {
                var originals = inputs.ToDictionary(input => input.Id);
                _draft = _draft with { Senses = _draft.Senses.Select(sense => sense.Id != _senseId ? sense : sense with
                {
                    Examples = sense.Examples.Select(example => originals.TryGetValue(example.Id, out var original)
                        && CanonicalStringComparer.Instance.Equals(example.Text, original.Text)
                        ? example with { Ranges = ranges[example.Id] } : example).ToArray(),
                }).ToArray() };
                _rangeInputs.Clear();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            await PublishRangesAsync(revision, cancellation.Token, () =>
            {
                _rangeFailed = true; FailureText.Text = new VocabularyException(VocabularyFailure.InvalidContent).Message;
                FailureText.Visibility = Visibility.Visible;
            }).ConfigureAwait(false);
        }
        finally { cancellation.Dispose(); }
    }
    private async Task PublishRangesAsync(long revision, CancellationToken token, Action publish)
    {
        if (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
        try
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_retired || revision != _rangeRevision || token.IsCancellationRequested) return;
                publish(); _reanchoring = false; _rangeCancellation = null; UpdateAdmission();
            }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
}
