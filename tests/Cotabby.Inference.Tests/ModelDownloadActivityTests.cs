using Cotabby.Inference;
namespace Cotabby.Inference.Tests;
public sealed class ModelDownloadActivityTests
{
    [Fact]
    public void ConcurrentLeasesPublishOnlyActualLifetimes()
    {
        var activity = new ModelDownloadActivity(); int changes = 0; activity.Changed += () => changes++;
        using var a = activity.Begin("a.gguf"); using var b = activity.Begin("A.gguf");
        Assert.True(activity.IsDownloading("a.gguf")); a.Dispose(); Assert.True(activity.IsDownloading("a.gguf"));
        b.Dispose(); Assert.False(activity.IsDownloading("a.gguf")); Assert.Equal(4, changes);
    }
}
