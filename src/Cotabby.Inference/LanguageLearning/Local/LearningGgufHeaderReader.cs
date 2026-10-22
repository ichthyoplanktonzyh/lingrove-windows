using System.Buffers.Binary;
using System.Text;
using Cotabby.Core.LanguageLearning;

namespace Cotabby.Inference.LanguageLearning.Local;

/// <summary>
/// Reads the same bounded GGUF header as Mac LearningLocalPrompt.modelMetadata. The learning
/// runtime invokes it off-thread before loading weights; Core receives only three immutable values.
/// LLamaSharp's metadata helpers allocate the full reported value before returning it, so their
/// post-read length checks cannot enforce this boundary for an untrusted local file.
/// </summary>
internal static class LearningGgufHeaderReader
{
    private const int MaximumStringBytes = 16_000_000;
    private const int MaximumHeaderBytes = 32_000_000;

    public static LearningGgufMetadata ReadFile(string path, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.SequentialScan);
        return Read(file, ct);
    }

    /// <summary>Leaves the caller's stream open; synthetic headers exercise this exact parser in tests.</summary>
    internal static LearningGgufMetadata Read(Stream source, CancellationToken ct)
    {
        using var limited = new ReadLimitStream(source, MaximumHeaderBytes);
        using var buffered = new BufferedStream(limited, 65_536);
        var cursor = new HeaderCursor(buffered, ct);
        if (!cursor.Bytes(4).AsSpan().SequenceEqual("GGUF"u8)
            || cursor.Integer(4) is not (2 or 3)) throw InvalidMetadata();
        _ = cursor.Integer(8); // Tensor count is not needed to resolve a text template.
        ulong count = cursor.Integer(8);
        if (count > 100_000) throw InvalidMetadata();

        string? architecture = null, name = null, chatTemplate = null;
        for (ulong index = 0; index < count; index++)
        {
            string key = cursor.Text();
            ulong type = cursor.Integer(4);
            if (type == 8 && key == "general.architecture") architecture = cursor.Text();
            else if (type == 8 && key == "general.name") name = cursor.Text();
            else if (type == 8 && key == "tokenizer.chat_template") chatTemplate = cursor.Text();
            else cursor.Skip(type);

            // Match Mac's early exit exactly. Hunyuan needs its turn-format discriminator, and
            // Gemma 3 needs provenance; other architectures need not scan the vocabulary arrays.
            if (architecture is not null)
            {
                bool enough = architecture == "hunyuan-dense" ? chatTemplate is not null
                    : architecture != "gemma3" || name is not null;
                if (enough) return new LearningGgufMetadata(architecture, name, chatTemplate);
            }
        }

        return architecture is not null
            ? new LearningGgufMetadata(architecture, name, chatTemplate)
            : throw InvalidMetadata();
    }

    private static InvalidDataException InvalidMetadata() =>
        new("GGUF metadata is incomplete, unsupported, or exceeds the supported read limit.");

    private sealed class HeaderCursor(Stream input, CancellationToken ct)
    {
        private int _consumed;

        private void Read(Span<byte> destination)
        {
            ct.ThrowIfCancellationRequested();
            if (destination.Length > MaximumStringBytes || destination.Length > MaximumHeaderBytes - _consumed)
                throw InvalidMetadata();
            input.ReadExactly(destination);
            _consumed += destination.Length;
        }

        public byte[] Bytes(int count)
        {
            // Check before allocation, including when a tiny file declares a multi-gigabyte value.
            if (count < 0 || count > MaximumStringBytes || count > MaximumHeaderBytes - _consumed)
                throw InvalidMetadata();
            var bytes = new byte[count];
            Read(bytes);
            return bytes;
        }

        public ulong Integer(int count)
        {
            Span<byte> bytes = stackalloc byte[8];
            Read(bytes[..count]);
            return count switch
            {
                4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                _ => throw InvalidMetadata(),
            };
        }

        public string Text()
        {
            ulong count = Integer(8);
            if (count > MaximumStringBytes) throw InvalidMetadata();
            // Swift String(decoding:as:) replaces malformed UTF-8; preserve that parser behavior.
            return Encoding.UTF8.GetString(Bytes((int)count));
        }

        public void Skip(ulong type)
        {
            if (type == 8)
            {
                ulong length = Integer(8);
                if (length > MaximumStringBytes) throw InvalidMetadata();
                SkipBytes((int)length);
            }
            else if (type == 9)
            {
                ulong subtype = Integer(4), count = Integer(8);
                if (count > 1_000_000 || subtype == 9) throw InvalidMetadata();
                if (FixedSize(subtype) is { } size) SkipBytes(checked((int)count * size));
                else for (ulong index = 0; index < count; index++) Skip(subtype);
            }
            else if (FixedSize(type) is { } size) SkipBytes(size);
            else throw InvalidMetadata();
        }

        private void SkipBytes(int count)
        {
            if (count < 0 || count > MaximumStringBytes || count > MaximumHeaderBytes - _consumed)
                throw InvalidMetadata();
            Span<byte> scratch = stackalloc byte[512];
            while (count > 0)
            {
                int next = Math.Min(count, scratch.Length);
                Read(scratch[..next]);
                count -= next;
            }
        }

        private static int? FixedSize(ulong type) => type switch
        {
            0 or 1 or 7 => 1,
            2 or 3 => 2,
            4 or 5 or 6 => 4,
            10 or 11 or 12 => 8,
            _ => null,
        };
    }

    /// <summary>Caps physical read-ahead too, while leaving the caller's stream owned by its caller.</summary>
    private sealed class ReadLimitStream(Stream inner, int maximum) : Stream
    {
        private int _remaining = maximum;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (_remaining == 0) return 0;
            int read = inner.Read(buffer[..Math.Min(buffer.Length, _remaining)]);
            _remaining -= read;
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
