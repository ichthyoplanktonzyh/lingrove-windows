namespace Cotabby.Core.Vocabulary;

public static partial class VocabularyExportFormatter
{
    /// <summary>Mac format dispatch over one reduced document. Loop checks belong to TXT/CSV;
    /// JSON has no in-render cancellation check. The package factory owns the initial check.</summary>
    public static byte[] Render(VocabularyExportDocument document, VocabularyExportConfiguration options,
        VocabularyExportTextLabels? labels = null, CancellationToken cancellationToken = default) => options.Format switch
    {
        VocabularyExportFormat.Text => RenderText(document, options.IsExpressionList, labels, cancellationToken),
        VocabularyExportFormat.Csv => RenderCsv(document, options, cancellationToken),
        VocabularyExportFormat.Json => RenderJson(document),
        _ => throw new ArgumentOutOfRangeException(nameof(options))
    };
}
