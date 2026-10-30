using Cotabby.App.UI;

namespace Cotabby.App.Wpf.Tests;

public sealed class LearningShowcasePlaybackTests
{
    [Fact]
    public void ScenesKeepMacOrderAndManualInteractionStopsHoverAutoplay()
    {
        Assert.Equal(new[] { "Writing", "Expression assistance", "Reading", "Dictionary", "Read Aloud", "Vocabulary", "Anki" },
            LearningShowcaseCatalog.Scenes.Select(x => x.Title));
        Assert.Equal(new[] { 9, 4, 7, 6, 3, 5, 4 }, LearningShowcaseCatalog.Scenes.Select(x => x.Steps.Count));
        var playback = new LearningShowcasePlayback(); playback.Hover(true, false); Assert.True(playback.IsPlaying);
        Assert.True(playback.Tick()); Assert.Equal(1, playback.Step); playback.Interact(); Assert.False(playback.IsPlaying);
        playback.Hover(true, false); Assert.False(playback.IsPlaying);
        playback.Select(1); playback.Hover(true, false); Assert.True(playback.IsPlaying);
        playback.Hover(false, false); Assert.False(playback.IsPlaying);
        playback.Replay(true); Assert.False(playback.IsPlaying); Assert.Equal(0, playback.Step);
        playback.Go(999); Assert.Equal(3, playback.Step); playback.Play(); Assert.False(playback.Tick()); Assert.False(playback.IsPlaying);
    }
}
