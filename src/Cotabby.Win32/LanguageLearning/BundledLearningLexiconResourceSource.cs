using System.IO;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Win32.LanguageLearning;

/// <summary>
/// The platform's local bundle-file boundary. The composition root supplies one fixed resource
/// path; the shared service calls Load on its worker only. Each attempt closes its file/temporary
/// buffer, bounds both initial length and growth, and exports only decoded immutable values.
/// No URI resolution, download, resource refresh or implicit substitute data is supported.
/// </summary>
public sealed class BundledLearningLexiconResourceSource(string? resourcePath) : ILearningLexiconResourceSource
{
    /// <summary>
    /// Windows counterpart of Bundle.main.url(forResource:): an omitted bundle asset resolves to
    /// no URL and therefore MissingResource. An explicitly supplied but unreadable path remains
    /// a read failure. Resolution is lazy with the composition root's source registration.
    /// </summary>
    public static BundledLearningLexiconResourceSource FromBundleDirectory(string directory)
    {
        var path = Path.Combine(directory, "Resources", "LearningLexicon", "learning-expressions.json");
        return new(File.Exists(path) ? path : null);
    }

    public LearningLexiconResource Load()
    {
        if (resourcePath is null) throw new LearningLexiconException(LearningLexiconFailure.MissingResource);
        try
        {
            using var file = new FileStream(resourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > LearningLexiconResource.MaximumBytes) throw Unreadable();
            using var data = new MemoryStream((int)file.Length);
            var buffer = new byte[81_920];
            while (true)
            {
                // Read one extra byte at the cap to detect a file that grew after Length. Do not
                // trust metadata alone and do not allocate an unbounded ReadAllBytes buffer.
                int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length,
                    (long)LearningLexiconResource.MaximumBytes + 1 - data.Length));
                if (count == 0) break;
                if (data.Length + count > LearningLexiconResource.MaximumBytes) throw Unreadable();
                data.Write(buffer, 0, count);
            }
            if (!data.TryGetBuffer(out var bytes)) throw Unreadable();
            return LearningLexiconResourceDecoder.Decode(new ReadOnlyMemory<byte>(bytes.Array!, bytes.Offset, (int)data.Length));
        }
        catch (LearningLexiconException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { throw Unreadable(); }
    }
    private static LearningLexiconException Unreadable() => new(LearningLexiconFailure.UnreadableResource);
}
