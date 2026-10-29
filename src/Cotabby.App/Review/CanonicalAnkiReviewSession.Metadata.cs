using Cotabby.Core.Review;

namespace Cotabby.App.Review;

/// <summary>Sheet metadata is a replaceable read, separate from Send's operation lease. Each
/// await rechecks its captured request/binding/model/connection so an older selection cannot
/// populate a new draft. Reading templates never upgrades or creates the remote model.</summary>
internal sealed partial class CanonicalAnkiReviewSession
{
    internal async Task LoadMetadataAsync(CancellationToken cancellationToken = default)
    {
        Guid request = Guid.NewGuid(); Guid? selectedId = BindingId; string selectedModel = Model;
        _metadataId = request; Loading = true; AvailableFields = []; Message = null; Changed();
        AnkiConnection? requestedConnection = null;
        bool IsCurrent() { try { return _metadataId == request && BindingId == selectedId && Same(Model, selectedModel) && requestedConnection is not null && Controller.Settings.Connection().Equals(requestedConnection); } catch { return false; } }
        try
        {
            var connection = Controller.Settings.Connection(); requestedConnection = connection;
            var catalog = await Controller.FetchCatalogAsync(connection, cancellationToken);
            if (!IsCurrent()) return;
            var fields = await Controller.CanonicalFieldsAsync(selectedModel, connection, false, null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            bool currentPreset, safeGap, legacyPreset; string css = AnkiNoteTemplate.Css;
            if (Same(selectedModel, AnkiReviewPolicy.PresetName) && Has(catalog.Models, selectedModel))
            {
                var templates = await Controller.CallAsync("modelTemplates", Params(("modelName", new AnkiValue.Text(selectedModel))), connection, cancellationToken);
                if (!IsCurrent()) return;
                var style = await Controller.CallAsync("modelStyling", Params(("modelName", new AnkiValue.Text(selectedModel))), connection, cancellationToken);
                if (!IsCurrent()) return;
                currentPreset = templates.Equals(new AnkiValue.Map(AnkiNoteTemplate.Templates)) && Enum.GetValues<ReviewField>().All(f => Has(fields, FieldName(f)));
                safeGap = Same(templates.Object?.GetValueOrDefault("Gap")?.Object?.GetValueOrDefault("Front")?.String, AnkiNoteTemplate.GapFront);
                legacyPreset = templates.Equals(new AnkiValue.Map(AnkiNoteTemplate.LegacyTemplates));
                css = style.Object?.GetValueOrDefault("css")?.String ?? AnkiNoteTemplate.Css;
            }
            else
            {
                currentPreset = Same(selectedModel, AnkiReviewPolicy.PresetName); safeGap = currentPreset; legacyPreset = false;
            }
            if (!IsCurrent()) return;
            Catalog = catalog; AvailableFields = fields; CurrentPresetTemplate = currentPreset; SafeGapFront = safeGap; LegacyPresetTemplate = legacyPreset; PresetCss = css;
            if (Binding is null && Plan.Deck is null && !Controller.Settings.HasSavedDeck && !Has(catalog.Decks, Deck) && catalog.Decks.FirstOrDefault() is { } first) Deck = first;
            Changed();
        }
        catch (Exception error) { if ((requestedConnection is null ? _metadataId == request && BindingId == selectedId && Same(Model, selectedModel) : IsCurrent()) && !cancellationToken.IsCancellationRequested) { Message = error.Message; Changed(); } }
        finally { if (_metadataId == request) { Loading = false; Changed(); } }
    }
    internal async Task UpdateTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (Busy) return;
        Busy = true; Changed();
        try
        {
            RequireNoAudio();
            var draft = CaptureSendState();
            var scope = await Controller.CanonicalScopeAsync(cancellationToken);
            await Controller.UpdateCanonicalPresetTemplateAsync(cancellationToken, async () =>
            {
                RequireNoAudio(); var current = await CurrentAsync(cancellationToken);
                if (ReviewValue(current.Sense, CardId) != _baseline) throw Conflict();
                await Controller.CanonicalVerifyAsync(scope, cancellationToken);
                RequireNoAudio(); if (!MatchesSendState(draft)) throw Conflict();
            });
            await LoadMetadataAsync(cancellationToken);
        }
        catch (Exception error) { Message = error is OperationCanceledException ? null : error.Message; Changed(); }
        finally { Busy = false; Changed(); }
    }
}
