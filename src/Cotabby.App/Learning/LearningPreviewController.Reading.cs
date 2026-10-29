using Cotabby.App.Settings;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.LanguageLearning.Reading;

namespace Cotabby.App.Learning;

internal sealed partial class LearningPreviewController
{
    /// <summary>Freeze the existing translation profile for a separate explicit reading request.
    /// The facade shares the translation runtime, never the completion or explanation runtime.</summary>
    internal Func<ReadingSelection, LearningLanguage?, LearningLanguage, Action<string>, CancellationToken, Task<string>>
        ReadingTranslator(AppSettings settings, int credentialRevision)
    {
        var profile = LearningRuntimeSettings.From(settings, credentialRevision);
        return async (selection, source, target, partial, token) =>
        {
            var options = profile.Options with { SourceLanguage = source, TargetLanguage = target };
            var engine = CreateEngine(profile);
            try
            {
                var id = selection.Id.ToString();
                var request = engine is ILearningTranslationRequestPlanner planner
                    ? await planner.PlanAsync(id, selection.Text, options, token).ConfigureAwait(false)
                    : LearningTranslationRequestFactory.Create(id, selection.Text, options);
                token.ThrowIfCancellationRequested();
                if (request.RequestId != id || request.Source != selection.Text || request.Options != options)
                    throw new LearningTranslationException(LearningTranslationFailure.InvalidOptions);
                request.Admission = selection.Admission;
                var stream = new LearningExpressionOutputPolicy.Stream();
                object gate = new();
                Exception? streamFailure = null;
                var result = await engine.TranslateAsync(request, delta =>
                {
                    lock (gate)
                    {
                        try
                        {
                            if (streamFailure is not null) throw streamFailure;
                            var prefix = stream.Append(delta);
                            if (prefix is not null) partial(prefix);
                        }
                        catch (Exception error) { streamFailure ??= error; throw; }
                    }
                }, token).ConfigureAwait(false);
                lock (gate) if (streamFailure is not null) throw new LearningTranslationException(LearningTranslationFailure.InvalidOutput);
                return result;
            }
            finally { await DisposeEngineAsync(engine).ConfigureAwait(false); }
        };
    }
}
