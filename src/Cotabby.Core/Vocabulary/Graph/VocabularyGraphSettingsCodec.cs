namespace Cotabby.Core.Vocabulary;

/// <summary>The original graph preference's complete Codable payload. Reuses the existing
/// Foundation-compatible UTF/required-field/first-duplicate-key boundary, without involving an
/// archive. A malformed preference becomes absent so the presentation can use its original defaults.</summary>
public static class VocabularyGraphSettingsCodec
{
    private sealed record Wire(double NodeSize, double LinkWidth, string Labels, double Center, double Repulsion, double LinkStrength, double LinkDistance);
    public static VocabularyGraphSettings? Decode(ReadOnlyMemory<byte> data)
    {
        try
        {
            var wire = VocabularyPayloadCodec.DecodePortableValue<Wire>(data);
            VocabularyGraphLabels? labels = wire.Labels switch { "automatic" => VocabularyGraphLabels.Automatic, "all" => VocabularyGraphLabels.All, "focused" => VocabularyGraphLabels.Focused, _ => null };
            if (labels is null || !new[] { wire.NodeSize, wire.LinkWidth, wire.Center, wire.Repulsion, wire.LinkStrength, wire.LinkDistance }.All(double.IsFinite)) return null;
            return new() { NodeSize = wire.NodeSize, LinkWidth = wire.LinkWidth, Labels = labels.Value, Center = wire.Center,
                Repulsion = wire.Repulsion, LinkStrength = wire.LinkStrength, LinkDistance = wire.LinkDistance };
        }
        catch (VocabularyException) { return null; }
    }
    public static byte[] EncodeNormalized(VocabularyGraphSettings settings)
    {
        var safe = settings.Normalized;
        return VocabularyPayloadCodec.EncodePortableValue(new Wire(safe.NodeSize, safe.LinkWidth,
            safe.Labels switch { VocabularyGraphLabels.All => "all", VocabularyGraphLabels.Focused => "focused", _ => "automatic" },
            safe.Center, safe.Repulsion, safe.LinkStrength, safe.LinkDistance));
    }
}
