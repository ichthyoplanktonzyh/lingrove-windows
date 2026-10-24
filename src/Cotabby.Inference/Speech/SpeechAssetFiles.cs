using System.IO;

namespace Cotabby.Inference.Speech;

/// <summary>One path inspection, separate from settings rules and synthesis. Standardized URL
/// equality is lexical; readability/length describe this observation, not a lease on asset bytes.
/// A provider must still handle replacement or access failure when it opens a frozen request.</summary>
public sealed record SpeechAssetFile(Uri Url, string Filename, string Extension, bool Readable, long? Size);

/// <summary>The settings owner borrows this filesystem boundary; tests supply known observations
/// while Windows uses its own path normalization and access rules. It never downloads a model.</summary>
public interface ISpeechAssetFiles { SpeechAssetFile Inspect(string path); }

/// <summary>Windows-compatible file inspection without loading weights/audio. Directory readability
/// is kept distinct from synthesis validity, matching the source's isReadableFile admission.</summary>
public sealed class SpeechAssetFiles : ISpeechAssetFiles
{
    public SpeechAssetFile Inspect(string path)
    {
        string full = Path.GetFullPath(path); bool readable; long? size = null;
        try
        {
            if (Directory.Exists(full)) readable = true;
            else { using var input = File.OpenRead(full); readable = true; size = input.Length; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { readable = false; }
        return new(new Uri(full), Path.GetFileName(full), Path.GetExtension(full).TrimStart('.'), readable, size);
    }
}
