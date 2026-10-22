using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Inference.LanguageLearning.Local;

namespace Cotabby.Inference.Tests.LanguageLearning;

public sealed class LearningGgufHeaderReaderTests
{
    [Fact]
    public void HunyuanFormatDiscriminatorAfterVocabularyArrayIsReadWithoutGuessing()
    {
        using var header = Header(3, writer =>
        {
            TextField(writer, "general.architecture", "hunyuan-dense");
            Key(writer, "tokenizer.tokens", 9);
            writer.Write(8u);
            writer.Write(2ul);
            Text(writer, "first-token");
            Text(writer, "second-token");
            TextField(writer, "tokenizer.chat_template", "turns end with <|extra_0|>");
        });

        var metadata = LearningGgufHeaderReader.Read(header, CancellationToken.None);

        Assert.Equal(LearningChatTemplate.HyMt7,
            LearningLocalPrompt.ResolveTemplate(LearningChatTemplate.Automatic, metadata, "renamed.gguf"));
        Assert.True(header.CanRead); // The parser does not take ownership of its caller's stream.
    }

    [Fact]
    public void TruncatedPresentDiscriminatorDoesNotBecomeAnAbsentTemplate()
    {
        using var header = Header(2, writer =>
        {
            TextField(writer, "general.architecture", "hunyuan-dense");
            Key(writer, "tokenizer.chat_template", 8);
            writer.Write(100ul); // The declared value is absent, not a legitimate missing key.
        });

        Assert.ThrowsAny<IOException>(() => LearningGgufHeaderReader.Read(header, CancellationToken.None));
    }

    [Fact]
    public void OversizedStringIsRejectedBeforeReadingOrAllocatingItsDeclaredContents()
    {
        using var header = Header(1, writer =>
        {
            Key(writer, "general.architecture", 8);
            writer.Write(16_000_001ul);
        });

        Assert.Throws<InvalidDataException>(() => LearningGgufHeaderReader.Read(header, CancellationToken.None));
    }

    [Fact]
    public void NestedArraysAreRejectedLikeThePinnedMacParser()
    {
        using var header = Header(1, writer =>
        {
            Key(writer, "unknown.nested", 9);
            writer.Write(9u);
            writer.Write(1ul);
        });

        Assert.Throws<InvalidDataException>(() => LearningGgufHeaderReader.Read(header, CancellationToken.None));
    }

    [Fact]
    public void EarlyExitOnlyOccursWhenTheArchitectureHasItsRequiredEvidence()
    {
        using var header = Header(100_000, writer => TextField(writer, "general.architecture", "qwen3"));

        var metadata = LearningGgufHeaderReader.Read(header, CancellationToken.None);

        Assert.Equal("qwen3", metadata.Architecture);
    }

    [Fact]
    public void CancellationIsCheckedBeforeHeaderWork()
    {
        using var header = Header(0, _ => { });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => LearningGgufHeaderReader.Read(header, cancelled.Token));
    }

    [Fact]
    public void ExcessiveEntryCountIsRejectedBeforeLookingForAKey()
    {
        using var header = Header(100_001, _ => { });
        Assert.Throws<InvalidDataException>(() => LearningGgufHeaderReader.Read(header, CancellationToken.None));
    }

    [Fact]
    public void ActuallyAbsentHunyuanDiscriminatorPreservesTheOlderTemplate()
    {
        using var header = Header(1, writer => TextField(writer, "general.architecture", "hunyuan-dense"));
        var metadata = LearningGgufHeaderReader.Read(header, CancellationToken.None);
        Assert.Null(metadata.ChatTemplate);
        Assert.Equal(LearningChatTemplate.HyMt,
            LearningLocalPrompt.ResolveTemplate(LearningChatTemplate.Automatic, metadata));
    }

    private static MemoryStream Header(ulong entries, Action<BinaryWriter> content)
    {
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("GGUF"u8);
            writer.Write(3u);
            writer.Write(0ul);
            writer.Write(entries);
            content(writer);
        }
        stream.Position = 0;
        return stream;
    }

    private static void Key(BinaryWriter writer, string key, uint type)
    {
        Text(writer, key);
        writer.Write(type);
    }

    private static void TextField(BinaryWriter writer, string key, string value)
    {
        Key(writer, key, 8);
        Text(writer, value);
    }

    private static void Text(BinaryWriter writer, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((ulong)bytes.Length);
        writer.Write(bytes);
    }
}
