namespace Cotabby.Win32.Speech;

/// <summary>Creates an inert serialized renderer; the WASAPI endpoint is opened only on Prepare.</summary>
public static class WindowsSpeechRendering
{
    public static WindowsSpeechPCMRenderer CreateRenderer() => new(new WindowsWasapiSpeechDeviceFactory());
}
