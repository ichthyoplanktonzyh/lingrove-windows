using Cotabby.Core.Focus;

namespace Cotabby.Core.Tests.Focus;

public sealed class CompositionEvidencePolicyTests
{
    [Fact]
    public void TextCaptureRequiresTwoMatchingPositiveObservations()
    {
        var clear = (InputCompositionState.Clear, 4L);
        var composing = (InputCompositionState.Composing, 4L);
        Assert.Equal(clear, CompositionEvidencePolicy.Reconcile(clear, clear));
        Assert.Equal(composing, CompositionEvidencePolicy.Reconcile(composing, composing));
        Assert.Equal(default, CompositionEvidencePolicy.Reconcile(clear, composing));
        Assert.Equal(default, CompositionEvidencePolicy.Reconcile(clear, (InputCompositionState.Clear, 5L)));
        Assert.Equal(default, CompositionEvidencePolicy.Reconcile(default, default));
        Assert.Equal(default, CompositionEvidencePolicy.Reconcile((InputCompositionState.Unknown, 4L), (InputCompositionState.Unknown, 4L)));
    }
}
