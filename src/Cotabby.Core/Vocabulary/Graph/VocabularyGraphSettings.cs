namespace Cotabby.Core.Vocabulary;

public enum VocabularyGraphLabels { Automatic, All, Focused }

/// <summary>Original appearance/force preferences, independent of notebook learning content.
/// Normalization keeps damaged/nonfinite numeric defaults away from the eventual layout solver.
/// This value owns no disk or simulation; a separate codec handles its Codable payload.</summary>
public sealed record VocabularyGraphSettings
{
    public double NodeSize { get; init; } = 17;
    public double LinkWidth { get; init; } = 1;
    public VocabularyGraphLabels Labels { get; init; } = VocabularyGraphLabels.Automatic;
    public double Center { get; init; } = .008;
    public double Repulsion { get; init; } = 320;
    public double LinkStrength { get; init; } = .14;
    public double LinkDistance { get; init; } = 105;
    public VocabularyGraphSettings Normalized => this with
    {
        // Swift's closed enum cannot represent a numeric outsider; .NET can, so close that
        // extra boundary before preferences or a label picker can observe an invalid case.
        Labels = Enum.IsDefined(Labels) ? Labels : VocabularyGraphLabels.Automatic,
        NodeSize = Bound(NodeSize, 8, 28, 17), LinkWidth = Bound(LinkWidth, .5, 3, 1), Center = Bound(Center, 0, .08, .008),
        Repulsion = Bound(Repulsion, 10, 1200, 320), LinkStrength = Bound(LinkStrength, 0, .7, .14), LinkDistance = Bound(LinkDistance, 60, 240, 105)
    };
    private static double Bound(double value, double minimum, double maximum, double fallback) => double.IsFinite(value) ? Math.Min(maximum, Math.Max(minimum, value)) : fallback;
    // Swift's numeric equality keeps NaN unequal before normalization; Double.Equals would
    // otherwise make two corrupt raw settings compare equal and suppress a caller's repair.
    public bool Equals(VocabularyGraphSettings? other) => other is not null && NodeSize == other.NodeSize && LinkWidth == other.LinkWidth
        && Labels == other.Labels && Center == other.Center && Repulsion == other.Repulsion && LinkStrength == other.LinkStrength && LinkDistance == other.LinkDistance;
    public override int GetHashCode() => HashCode.Combine(NodeSize, LinkWidth, Labels, Center, Repulsion, LinkStrength, LinkDistance);
}
