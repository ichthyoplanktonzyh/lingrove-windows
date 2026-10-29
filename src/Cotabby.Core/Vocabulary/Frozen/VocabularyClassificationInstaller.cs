using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Stages the bundled directory against the canonical document's actual semantic node
/// identities. The archive owner decides whether to commit the returned candidate and revision;
/// this value operation neither performs persistence nor infers memberships.</summary>
public static class VocabularyClassificationInstaller
{
    public static (VocabularyDocument Document, bool Installed) Install(VocabularyDocument document, VocabularyClassificationCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(catalog);
        if (document.Semantics is null || document.Semantics.Nodes.Any(node => node is null) || document.Classification is null)
            throw new VocabularyException(VocabularyFailure.InvalidContent);
        var ids = document.Semantics.Nodes.Select(node => node.Id).ToHashSet();
        var (classification, installed) = catalog.InstallForNodeIds(document.Classification, ids);
        return installed ? (document with { Classification = classification }, true) : (document, false);
    }

    /// <summary>Disabled owners perform no resource load; failed loads/installations remain
    /// retryable because no candidate or cache is published by this pure boundary.</summary>
    public static (VocabularyDocument Document, bool Installed) InstallBundled(VocabularyDocument document, bool enabled)
        => Install(document, enabled, VocabularyClassificationCatalog.Bundled);

    /// <summary>Loads only when enabled. The caller may cache successful catalogs, but failed
    /// resource loading is never cached or converted into a successful empty installation.</summary>
    public static (VocabularyDocument Document, bool Installed) Install(VocabularyDocument document, bool enabled, Func<VocabularyClassificationCatalog> loadCatalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(loadCatalog);
        return enabled ? Install(document, loadCatalog()) : (document, false);
    }
}
