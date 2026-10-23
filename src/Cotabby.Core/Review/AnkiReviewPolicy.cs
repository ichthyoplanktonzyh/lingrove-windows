using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>
/// The pure review-merge subset from Mac: a future sense transaction keeps the target plan and
/// all source history/remote associations. No endpoint validation, connection or send is implied.
/// </summary>
public static class AnkiReviewPolicy
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
