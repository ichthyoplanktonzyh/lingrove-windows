using Cotabby.Core.Support;

namespace Cotabby.Core.Vocabulary;

/// <summary>
/// Pure counterpart of Mac VocabularyArchive.imageReferences: the complete notebook owns media
/// through sense content, all retained review layouts and global templates. Archive GC, backup and
/// restore share this walk so one effect cannot silently omit a retained card/template image.
/// Duplicates remain in source order; each operation decides whether it needs a set or an ordered list.
/// </summary>
public static class VocabularyArchiveMediaPolicy
{
    public static IReadOnlyList<VocabularyImageReference> References(VocabularyDocument document) => CollectionValues.List(
        document.Entries.SelectMany(entry => entry.Senses).SelectMany(sense => VocabularyContentPolicy.Images(sense.Content ?? [])
            .Concat(sense.ReviewLayouts.SelectMany(layout => VocabularyContentPolicy.Images(layout.CustomContent))))
        .Concat((document.ReviewTemplates ?? []).SelectMany(template => VocabularyContentPolicy.Images(template.Layout.CustomContent))));
}
