using Cotabby.Core.Support;

namespace Cotabby.Core.Review;

/// <summary>One sheet-owned three-way comparison result. This detached value retains the actual
/// field name and local/remote text for an explicit choice; it is never a notebook receipt or write.</summary>
public sealed record AnkiFieldConflict(string Id, string Local, string Remote)
{
    public bool Equals(AnkiFieldConflict? other) => other is not null && CollectionValues.Same(Id, other.Id)
        && CollectionValues.Same(Local, other.Local) && CollectionValues.Same(Remote, other.Remote);
    public override int GetHashCode() => HashCode.Combine(CollectionValues.StringHash(Id), CollectionValues.StringHash(Local), CollectionValues.StringHash(Remote));
}
