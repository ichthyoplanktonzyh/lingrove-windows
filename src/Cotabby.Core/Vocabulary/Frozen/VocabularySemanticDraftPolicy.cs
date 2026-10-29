using Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Vocabulary.Frozen;

/// <summary>Detached source graph edits; the archive validates and acknowledges the candidate.</summary>
public static class VocabularySemanticDraftPolicy
{
    public static VocabularyDocument UpdateSemantics(VocabularyDocument document, VocabularySemanticIndex edited, VocabularySemanticIndex baseline)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(edited); ArgumentNullException.ThrowIfNull(baseline);
        VocabularyArchiveValidation.ValidateRepresentability(document with { Semantics = edited });
        VocabularyArchiveValidation.ValidateRepresentability(document with { Semantics = baseline });
        var merged = MergeSemantics(edited, baseline, document.Semantics);
        var removed = baseline.Nodes.Select(value => value.Id).Except(merged.Nodes.Select(value => value.Id)).ToHashSet();
        var classification = document.Classification with { Memberships = document.Classification.Memberships.Where(value => !removed.Contains(value.NodeId)).ToArray() };
        VocabularySemanticPolicy.Validate(merged, document.Entries);
        return document with { Semantics = merged, Classification = classification };
    }
    private static VocabularySemanticIndex MergeSemantics(VocabularySemanticIndex edited, VocabularySemanticIndex baseline, VocabularySemanticIndex current)
    {
        var merged = current with {
            Nodes = MergeRows(edited.Nodes, baseline.Nodes, current.Nodes, value => value.Id),
            Patterns = MergeRows(edited.Patterns, baseline.Patterns, current.Patterns, value => value.Id),
            Mappings = MergeRows(edited.Mappings, baseline.Mappings, current.Mappings, value => value.Id),
            Relations = MergeRows(edited.Relations, baseline.Relations, current.Relations, value => value.Id),
            Cues = MergeRows(edited.Cues, baseline.Cues, current.Cues, value => value.Id),
            CueTargets = MergeRows(edited.CueTargets, baseline.CueTargets, current.CueTargets, value => value.Id) };
        return merged;
    }
    public static VocabularyDocument UpdateMeaningOrganization(VocabularyDocument document, VocabularySemanticIndex editedSemantics, VocabularySemanticIndex semanticBaseline, VocabularyClassificationIndex editedClassification, VocabularyClassificationIndex classificationBaseline)
    {
        VocabularyArchiveValidation.ValidateRepresentability(document with { Semantics = editedSemantics, Classification = editedClassification });
        VocabularyArchiveValidation.ValidateRepresentability(document with { Semantics = semanticBaseline, Classification = classificationBaseline });
        var semantics = MergeSemantics(editedSemantics, semanticBaseline, document.Semantics);
        var classification = VocabularyClassificationPolicy.Merge(editedClassification, classificationBaseline, document.Classification);
        VocabularySemanticPolicy.Validate(semantics, document.Entries);
        VocabularyClassificationPolicy.ValidateNodeIds(classification, semantics.Nodes.Select(value => value.Id).ToHashSet());
        return document with { Semantics = semantics, Classification = classification };
    }
    public static VocabularyDocument UpdateClassification(VocabularyDocument document, VocabularyClassificationIndex edited, VocabularyClassificationIndex baseline)
    {
        ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(edited); ArgumentNullException.ThrowIfNull(baseline);
        VocabularyArchiveValidation.ValidateRepresentability(document with { Classification = edited });
        VocabularyArchiveValidation.ValidateRepresentability(document with { Classification = baseline });
        var merged = VocabularyClassificationPolicy.Merge(edited, baseline, document.Classification);
        VocabularyClassificationPolicy.ValidateNodeIds(merged, document.Semantics.Nodes.Select(value => value.Id).ToHashSet());
        return document with { Classification = merged };
    }
    private static IReadOnlyList<T> MergeRows<T>(IReadOnlyList<T> edited, IReadOnlyList<T> baseline, IReadOnlyList<T> current, Func<T, Guid> id) where T : class
    {
        if (edited.Any(value => value is null) || edited.Select(id).Distinct().Count() != edited.Count) throw Invalid();
        var result = current.ToList();
        foreach (var value in edited)
        {
            var before = baseline.FirstOrDefault(item => id(item) == id(value));
            if (Equals(before, value)) continue;
            int index = result.FindIndex(item => id(item) == id(value));
            if (index >= 0)
            {
                if (!Equals(result[index], before) && !Equals(result[index], value)) throw Conflict();
                result[index] = value;
            }
            else { if (before is not null) throw Conflict(); result.Add(value); }
        }
        foreach (var before in baseline.Where(item => !edited.Any(value => id(value) == id(item))))
        {
            var live = result.FirstOrDefault(item => id(item) == id(before));
            if (live is not null && !Equals(live, before)) throw Conflict();
            result.RemoveAll(item => id(item) == id(before));
        }
        return result;
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    private static VocabularyException Conflict() => new(VocabularyFailure.EditConflict);
}
