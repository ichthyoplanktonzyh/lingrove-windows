using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>
/// Pure review interoperability rules from Mac. Merge retains target ownership/history; the
/// companion field policy projects validated material without a Store, HTTP client or speech runtime.
/// </summary>
public static partial class AnkiReviewPolicy
{
    public static VocabularyReview? Merged(VocabularyReview? target, VocabularyReview? source)
    {
        if (target is null) return source;
        if (source is null) return target;
        var bindings = target.Bindings.ToList();
        foreach (var binding in source.Bindings)
            if (!bindings.Any(existing => CollectionValues.Same(existing.Endpoint, binding.Endpoint)
                && CollectionValues.Same(existing.Profile, binding.Profile)
                && (existing.NoteId is not null && existing.NoteId == binding.NoteId || existing.OriginSenseId == binding.OriginSenseId)))
                bindings.Add(binding);
        return target with { Bindings = bindings, PreservedPlans = target.PreservedPlans.Concat(new[] { source.Plan }).Concat(source.PreservedPlans).ToArray(),
            ActiveBindingId = bindings.Count > 1 ? null : bindings.FirstOrDefault()?.Id };
    }
}
