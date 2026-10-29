using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Vocabulary.Resources;

/// <summary>Pure semantic integrity over joined entries; evidence IDs refer to interpretation IDs.</summary>
public static class VocabularySemanticPolicy
{
    public static void Validate(VocabularySemanticIndex index, IReadOnlyList<VocabularyEntry> entries)
    {
        if (index is null || entries is null || index.Nodes.Count > 100_000 || index.Patterns.Count > 500_000 || index.Mappings.Count > 500_000
            || index.Relations.Count > 500_000 || index.Cues.Count > 500_000 || index.CueTargets.Count > 500_000) throw Invalid();
        var senses = new Dictionary<Guid, VocabularySense>(); var ids = new HashSet<Guid>();
        foreach (var entry in entries)
        {
            if (entry is null) throw Invalid(); ids.Add(entry.Id);
            foreach (var sense in entry.Senses) { if (sense is null || !senses.TryAdd(sense.Id, sense)) throw Archive(); ids.Add(sense.Id); foreach (var example in sense.Examples) { if (example is null) throw Invalid(); ids.Add(example.Id); } }
        }
        void Identify(Guid id) { if (!ids.Add(id)) throw Archive(); }
        void Text(string? text, bool required = false)
        {
            if (!VocabularyClassificationPolicy.Fits(text, 32_768) || required && VocabularyCapturePolicy.Normalized(text!).Length == 0) throw Invalid();
        }
        void Provenance(VocabularySemanticProvenance? p)
        {
            if (p is null || !Enum.IsDefined(p.Origin) || !Enum.IsDefined(p.Status)) throw Invalid(); Text(p.Source);
            if (p.Confidence is { } score && (!double.IsFinite(score) || score is < 0 or > 1)) throw Invalid();
        }
        var nodes = new Dictionary<Guid, VocabularyMeaningNode>();
        foreach (var node in index.Nodes)
        {
            if (node is null || !Enum.IsDefined(node.Kind)) throw Invalid(); Identify(node.Id); Text(node.Definition); Text(node.ScopeNote); Provenance(node.Provenance);
            if (node.Labels.Count is 0 or > 128 || node.Roles.Count > 64) throw Invalid();
            var preferred = new HashSet<LearningLanguage?>();
            foreach (var label in node.Labels)
            {
                if (label is null || label.Language is { } language && !Enum.IsDefined(language)) throw Invalid(); Identify(label.Id); Text(label.Text, true);
                if (label.Preferred && !preferred.Add(label.Language)) throw Invalid();
            }
            if (preferred.Count == 0) throw Invalid();
            foreach (var role in node.Roles) { if (role is null) throw Invalid(); Identify(role.Id); Text(role.Name, true); Text(role.Description); }
            nodes.Add(node.Id, node);
        }
        var patterns = new Dictionary<Guid, VocabularyUsagePattern>();
        foreach (var pattern in index.Patterns)
        {
            if (pattern is null || !Enum.IsDefined(pattern.Formality) || !Enum.IsDefined(pattern.Medium)) throw Invalid();
            Identify(pattern.Id); Text(pattern.Template, true); Text(pattern.Note); Text(pattern.Region); Provenance(pattern.Provenance);
            if (!senses.ContainsKey(pattern.SenseId) || pattern.Slots.Count > 64 || pattern.Collocations.Count > 128) throw Invalid();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in pattern.Slots) { if (slot is null) throw Invalid(); Identify(slot.Id); Text(slot.Name, true); Text(slot.Constraint); if (!names.Add(VocabularyCapturePolicy.Normalized(slot.Name))) throw Invalid(); }
            foreach (var value in pattern.Collocations) Text(value, true);
            patterns.Add(pattern.Id, pattern);
        }
        var evidenceBySense = senses.ToDictionary(pair => pair.Key, pair => pair.Value.Examples.Select(x => x.Id).ToHashSet());
        foreach (var mapping in index.Mappings)
        {
            if (mapping is null || !Enum.IsDefined(mapping.Relation)) throw Invalid(); Identify(mapping.Id); Text(mapping.Conditions); Text(mapping.Nuance); Provenance(mapping.Provenance);
            if (!nodes.TryGetValue(mapping.NodeId, out var node) || !senses.TryGetValue(mapping.SenseId, out var sense)
                || (node.Kind == VocabularyMeaningNodeKind.Concept) != (mapping.Relation == VocabularyMappingRelation.ExpressesContent)
                || mapping.PatternId is { } patternId && (!patterns.TryGetValue(patternId, out var pattern) || pattern.SenseId != mapping.SenseId)
                || mapping.EvidenceIds.Distinct().Count() != mapping.EvidenceIds.Count || !mapping.EvidenceIds.All(evidenceBySense[mapping.SenseId].Contains)
                || mapping.RoleBindings.Count > 64) throw Invalid();
            var slots = mapping.PatternId is { } pid ? patterns[pid].Slots : Array.Empty<VocabularyPatternSlot>(); var roles = new HashSet<Guid>();
            foreach (var binding in mapping.RoleBindings)
            {
                if (binding is null) throw Invalid(); Identify(binding.Id);
                if (!roles.Add(binding.RoleId) || !node.Roles.Any(x => x.Id == binding.RoleId) || !slots.Any(x => x.Id == binding.SlotId)) throw Invalid();
            }
        }
        var children = new Dictionary<Guid, List<Guid>>(); var incoming = new Dictionary<Guid, int>();
        foreach (var relation in index.Relations)
        {
            if (relation is null || !Enum.IsDefined(relation.Kind)) throw Invalid(); Identify(relation.Id); Text(relation.Note); Provenance(relation.Provenance);
            if (!nodes.TryGetValue(relation.SourceId, out var source) || !nodes.TryGetValue(relation.TargetId, out var target) || source.Id == target.Id
                || relation.Kind != VocabularyMeaningRelationKind.Related && source.Kind != target.Kind) throw Invalid();
            if (relation.Kind == VocabularyMeaningRelationKind.Narrower)
            {
                if (!children.TryGetValue(source.Id, out var list)) children[source.Id] = list = []; list.Add(target.Id);
                incoming.TryAdd(source.Id, 0); incoming[target.Id] = incoming.GetValueOrDefault(target.Id) + 1;
            }
        }
        var queue = new Queue<Guid>(incoming.Where(x => x.Value == 0).Select(x => x.Key)); var visited = 0;
        while (queue.TryDequeue(out var id)) { visited++; if (children.TryGetValue(id, out var list)) foreach (var child in list) if (--incoming[child] == 0) queue.Enqueue(child); }
        if (visited != incoming.Count) throw Invalid();
        var cues = new HashSet<Guid>();
        foreach (var cue in index.Cues) { if (cue is null || !Enum.IsDefined(cue.Kind) || cue.Language is { } l && !Enum.IsDefined(l)) throw Invalid(); Identify(cue.Id); Text(cue.Text, true); cues.Add(cue.Id); }
        var targets = new HashSet<(Guid, Guid)>();
        foreach (var target in index.CueTargets)
        {
            if (target is null) throw Invalid(); Identify(target.Id); Text(target.Conditions); Provenance(target.Provenance);
            if (!cues.Contains(target.CueId) || !nodes.ContainsKey(target.NodeId) || !targets.Add((target.CueId, target.NodeId))) throw Invalid();
        }
    }
    private static VocabularyException Invalid() => new(VocabularyFailure.InvalidContent);
    private static VocabularyException Archive() => new(VocabularyFailure.InvalidArchive);
}
