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
        try
        {
            var connection = Controller.Settings.Connection();
            var catalog = await Controller.FetchCatalogAsync(connection, cancellationToken);
            var fields = await Controller.CanonicalFieldsAsync(selectedModel, connection, false, null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_metadataId != request || BindingId != selectedId || !Same(Model, selectedModel) || !Controller.Settings.Connection().Equals(connection)) return;
            Catalog = catalog; AvailableFields = fields; Changed();
            if (Same(selectedModel, AnkiReviewPolicy.PresetName) && Has(catalog.Models, selectedModel))
            {
                var templates = await Controller.CallAsync("modelTemplates", Params(("modelName", new AnkiValue.Text(selectedModel))), connection, cancellationToken);
                if (_metadataId != request || BindingId != selectedId) return;
                var style = await Controller.CallAsync("modelStyling", Params(("modelName", new AnkiValue.Text(selectedModel))), connection, cancellationToken);
                if (_metadataId != request || BindingId != selectedId || !Controller.Settings.Connection().Equals(connection)) return;
                CurrentPresetTemplate = templates.Equals(new AnkiValue.Map(AnkiNoteTemplate.Templates)) && Enum.GetValues<ReviewField>().All(f => Has(fields, FieldName(f)));
                SafeGapFront = Same(templates.Object?.GetValueOrDefault("Gap")?.Object?.GetValueOrDefault("Front")?.String, AnkiNoteTemplate.GapFront);
                LegacyPresetTemplate = templates.Equals(new AnkiValue.Map(AnkiNoteTemplate.LegacyTemplates));
                PresetCss = style.Object?.GetValueOrDefault("css")?.String ?? AnkiNoteTemplate.Css;
            }
            else
            {
                CurrentPresetTemplate = Same(selectedModel, AnkiReviewPolicy.PresetName); SafeGapFront = CurrentPresetTemplate; LegacyPresetTemplate = false;
            }
            if (Binding is null && Plan.Deck is null && !Controller.Settings.HasSavedDeck && !Has(catalog.Decks, Deck) && catalog.Decks.FirstOrDefault() is { } first) Deck = first;
            Changed();
        }
        catch (Exception error) { if (_metadataId == request && !cancellationToken.IsCancellationRequested) { Message = error.Message; Changed(); } }
        finally { if (_metadataId == request) { Loading = false; Changed(); } }
    }
    internal async Task UpdateTemplateAsync(CancellationToken cancellationToken = default)
    {
        if (Busy) return;
        Busy = true; Changed();
        try
        {
            RequireNoAudio();
            var scope = await Controller.CanonicalScopeAsync(cancellationToken);
            await Controller.UpdateCanonicalPresetTemplateAsync(cancellationToken, async () =>
            {
                RequireNoAudio(); var current = await CurrentAsync(cancellationToken);
                if (ReviewValue(current.Sense, CardId) != _baseline) throw Conflict();
                await Controller.CanonicalVerifyAsync(scope, cancellationToken);
            });
            await LoadMetadataAsync(cancellationToken);
        }
        catch (Exception error) { Message = error is OperationCanceledException ? null : error.Message; Changed(); }
        finally { Busy = false; Changed(); }
    }
}
