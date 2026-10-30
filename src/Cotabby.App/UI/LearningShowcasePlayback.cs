namespace Cotabby.App.UI;

/// <summary>Mounted walkthrough state; scheduler and sample sessions remain owned by its view.
/// Manual interaction retires autoplay so a delayed scripted action cannot undo a user's choice.</summary>
internal sealed class LearningShowcasePlayback
{
    private bool _manuallyControlled, _hoveredPlayback;
    internal int Scene { get; private set; }
    internal int Step { get; private set; }
    internal bool IsPlaying { get; private set; }
    internal LearningShowcaseCatalog.Scene CurrentScene => LearningShowcaseCatalog.Scenes[Scene];
    internal LearningShowcaseCatalog.Step CurrentStep => CurrentScene.Steps[Step];

    internal void Select(int scene)
    {
        Pause(); Scene = Math.Clamp(scene, 0, LearningShowcaseCatalog.Scenes.Count - 1);
        Step = 0; _manuallyControlled = false;
    }
    internal void Go(int step)
    {
        Pause(); _manuallyControlled = true; Step = Math.Clamp(step, 0, CurrentScene.Steps.Count - 1);
    }
    internal void Previous() => Go(Step - 1);
    internal void Next() => Go(Step + 1);
    internal void Interact() { Pause(); _manuallyControlled = true; }
    internal void Hover(bool entering, bool reduceMotion)
    {
        if (entering && !_manuallyControlled && !reduceMotion) Play(true);
        else if (!entering && _hoveredPlayback) Pause();
    }
    internal void Play(bool hovered = false)
    {
        Pause(); _hoveredPlayback = hovered; IsPlaying = true;
        if (!hovered) _manuallyControlled = true;
    }
    internal void Replay(bool reduceMotion)
    {
        int scene = Scene; Select(scene); if (!reduceMotion) Play();
    }
    internal bool Tick()
    {
        if (!IsPlaying) return false;
        if (Step == CurrentScene.Steps.Count - 1) { Pause(); return false; }
        Step++; return true;
    }
    internal void Pause() { IsPlaying = false; _hoveredPlayback = false; }
}
