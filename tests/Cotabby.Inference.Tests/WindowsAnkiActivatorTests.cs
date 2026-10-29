using Cotabby.Win32.Review;

namespace Cotabby.Inference.Tests;

public sealed class WindowsAnkiActivatorTests
{
    private sealed class Windows : WindowsAnkiActivator.IAnkiWindows
    {
        internal readonly List<string> Effects = [];
        internal IReadOnlyList<WindowsAnkiActivator.Candidate> Candidates = [new(12, 34)];
        internal Func<WindowsAnkiActivator.Candidate, bool> Current = _ => true;
        internal bool Minimized;
        internal Action? AfterRestore;
        public IReadOnlyList<WindowsAnkiActivator.Candidate> FindExisting() => Candidates;
        public bool IsCurrent(WindowsAnkiActivator.Candidate candidate) => Current(candidate);
        public bool IsMinimized(nint window) => Minimized;
        public void Restore(nint window) { Effects.Add("restore:" + window); AfterRestore?.Invoke(); }
        public void TryForeground(nint window) => Effects.Add("foreground:" + window);
    }
    [Fact]
    public void ConstructionAndMissingApplicationHaveNoEffects()
    {
        var windows = new Windows { Candidates = [] }; var owner = new WindowsAnkiActivator(windows);
        Assert.Empty(windows.Effects); owner.ActivateExisting(); Assert.Empty(windows.Effects);
    }
    [Fact]
    public void ExistingMinimizedApplicationIsRestoredBeforeActivation()
    {
        var windows = new Windows { Minimized = true }; new WindowsAnkiActivator(windows).ActivateExisting();
        Assert.Equal(new[] { "restore:12", "foreground:12" }, windows.Effects);
    }
    [Fact]
    public void ReusedWindowAfterRestoreCannotReceiveForeground()
    {
        var windows = new Windows { Minimized = true }; windows.AfterRestore = () => windows.Current = _ => false;
        new WindowsAnkiActivator(windows).ActivateExisting(); Assert.Equal(new[] { "restore:12" }, windows.Effects);
    }
    [Fact]
    public void IneligibleCandidateIsSkippedAndOnlyOneExistingApplicationActivated()
    {
        var windows = new Windows { Candidates = [new(12, 34), new(56, 78), new(90, 12)], Current = value => value.ProcessId != 34 };
        new WindowsAnkiActivator(windows).ActivateExisting(); Assert.Equal(new[] { "foreground:56" }, windows.Effects);
    }
}
