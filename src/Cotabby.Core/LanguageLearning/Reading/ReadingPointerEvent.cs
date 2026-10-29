namespace Cotabby.Core.LanguageLearning.Reading;

public enum ReadingPointerKind { LeftDown, LeftUp, RightDown, Scroll }
/// <summary>Observation only. Coordinates are physical screen pixels; handlers may not block or suppress input.</summary>
public readonly record struct ReadingPointerEvent(ReadingPointerKind Kind, int X, int Y, int ClickCount, uint Time);
public interface IReadingPointerMonitor : IDisposable
{
    event Action<ReadingPointerEvent>? Pointer;
    void Start();
    void Stop();
}
