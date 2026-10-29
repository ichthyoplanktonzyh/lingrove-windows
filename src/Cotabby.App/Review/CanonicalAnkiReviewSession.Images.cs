using Cotabby.Core.Review;
using Cotabby.Core.Vocabulary;

namespace Cotabby.App.Review;

/// <summary>Image upload shares Send's profile/content guards and precedes every note write.
/// Live templates are checked again rather than trusting metadata cached when the sheet opened.
/// Remote media is never deleted on cancellation: a committed note may already reference it.</summary>
internal sealed partial class CanonicalAnkiReviewSession
{
    private async Task ValidateImageTemplateAsync(ReviewCardContent content, AnkiConnection connection, CancellationToken cancellationToken)
    {
        if (Same(Model, ReviewLayoutPolicy.Model))
        {
            var models = AnkiReviewController.Strings(await Controller.CallAsync("modelNames", Params(), connection, cancellationToken));
            await ValidateCurrentAsync(content, connection, cancellationToken);
            if (Has(models, Model))
            {
                var templates = await Controller.CallAsync("modelTemplates", Params(("modelName", new AnkiValue.Text(Model))), connection, cancellationToken);
                await ValidateCurrentAsync(content, connection, cancellationToken);
                if (!templates.Equals(new AnkiValue.Map(ReviewLayoutPolicy.Templates))) throw new AnkiReviewException("The Anki card template changed. Restore Front and Back before sending.");
            }
            return;
        }
        if (!Same(Model, AnkiReviewPolicy.PresetName) || !Plan.Gap || content.SenseContent.Count == 0) return;
        var names = AnkiReviewController.Strings(await Controller.CallAsync("modelNames", Params(), connection, cancellationToken));
        await ValidateCurrentAsync(content, connection, cancellationToken);
        if (!Has(names, Model)) return;
        var preset = await Controller.CallAsync("modelTemplates", Params(("modelName", new AnkiValue.Text(Model))), connection, cancellationToken);
        await ValidateCurrentAsync(content, connection, cancellationToken);
        if (!Same(preset.Object?.GetValueOrDefault("Gap")?.Object?.GetValueOrDefault("Front")?.String, AnkiNoteTemplate.GapFront))
            throw new AnkiReviewException("Update the Lingrove template before adding explanation content to gap cards.");
    }
    private async Task UploadImagesAsync(ReviewCardContent content, AnkiConnection connection, string profile, CancellationToken cancellationToken)
    {
        foreach (var image in VocabularyContentPolicy.Images(content.SenseContent).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] data;
            try { data = await Store.ReadImageAsync(image, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch { throw new AnkiReviewException("An explanation image is missing. Add it again before sending."); }
            await ValidateCurrentAsync(content, connection, cancellationToken);
            if (!Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw Conflict();
            await ValidateCurrentAsync(content, connection, cancellationToken);
            var stored = await Controller.CallAsync("storeMediaFile", Params(("filename", new AnkiValue.Text(image.Filename)), ("data", new AnkiValue.Text(Convert.ToBase64String(data)))), connection, cancellationToken);
            if (!Same(stored.String, image.Filename) || !Same(await Controller.ProfileAsync(connection, cancellationToken), profile)) throw AnkiReviewController.Invalid();
            await ValidateCurrentAsync(content, connection, cancellationToken);
        }
    }
}
