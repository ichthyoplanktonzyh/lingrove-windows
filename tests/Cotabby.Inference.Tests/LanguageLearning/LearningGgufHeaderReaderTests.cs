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

    [Theory]
    [InlineData("hunyuan-dense", null, false)]
    [InlineData("gemma3", "TranslateGemma", false)]
    [InlineData("qwen3", "General instruction model", true)]
    public void ExplanationGateUsesTheActualBoundedHeaderDespiteAManualGeneralTemplate(
        string architecture, string? name, bool allowed)
    {
        using var header = Header(name is null ? 1ul : 2ul, writer =>
        {
            TextField(writer, "general.architecture", architecture);
            if (name is not null) TextField(writer, "general.name", name);
        });
        var metadata = LearningGgufHeaderReader.Read(header, CancellationToken.None);
        var configuration = new LearningLocalModelConfiguration("renamed.gguf", LearningChatTemplate.ChatMl);
        if (allowed) Assert.Equal(LearningChatTemplate.ChatMl,
            LearningExplanationModelPolicy.ResolveLocalProfile(configuration, metadata).Template);
        else Assert.Equal(LearningExplanationFailure.InstructionModelRequired,
            Assert.Throws<LearningExplanationException>(() => LearningExplanationModelPolicy.ResolveLocalProfile(configuration, metadata)).Failure);
    }

    [Theory]
    [InlineData("hunyuan-dense", false)]
    [InlineData("qwen3", true)]
    public void ProductionPreflightReadsMetadataForManualExplanationBeforeAnyNativeLoad(string architecture, bool allowed)
    {
        using var folder = new ModelFolder(); string path = folder.ModelPath;
        try
        {
            using var header = Header(1, writer => TextField(writer, "general.architecture", architecture));
            File.WriteAllBytes(path, header.ToArray());
            var configuration = new LearningLocalModelConfiguration(path, LearningChatTemplate.ChatMl, requiresGeneralInstructions: true);
            if (allowed) Assert.Equal(architecture, LearningLocalRuntime.ReadMetadataForOperation(configuration, default).Architecture);
            else Assert.Equal(LearningExplanationFailure.InstructionModelRequired,
                Assert.Throws<LearningExplanationException>(() => LearningLocalRuntime.ReadMetadataForOperation(configuration, default)).Failure);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TranslationManualPreflightRetainsTheExistingMetadataBypass()
    {
        var configuration = new LearningLocalModelConfiguration("nonexistent-file.gguf", LearningChatTemplate.ChatMl);
        Assert.Equal(string.Empty, LearningLocalRuntime.ReadMetadataForOperation(configuration, default).Architecture);
    }

    [Fact]
    public void WindowsModelReadLeaseDeniesWritesAndReplacementUntilReleased()
    {
        using var folder = new ModelFolder(); string path = folder.ModelPath, destination = path + ".moved";
        try
        {
            using var header = Header(1, writer => TextField(writer, "general.architecture", "qwen3"));
            File.WriteAllBytes(path, header.ToArray());
            using (LearningLocalRuntime.OpenModelReadLease(path))
            {
                Assert.ThrowsAny<IOException>(() => File.WriteAllText(path, "replaced"));
                Assert.ThrowsAny<IOException>(() => File.Move(path, destination));
                Assert.Equal("qwen3", LearningLocalRuntime.ReadMetadataForOperation(
                    new(path, LearningChatTemplate.ChatMl, requiresGeneralInstructions: true), default).Architecture);
            }
            File.Move(path, destination);
            File.WriteAllText(destination, "released");
        }
        finally { File.Delete(path); File.Delete(destination); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeCleanupAlwaysAttemptsWeightsAndFileGuardPreservingTheFirstFailure(bool suppress)
    {
        var order = new List<string>(); var first = new InvalidOperationException("first");
        var context = new DisposalAction(() => { order.Add("context"); throw first; });
        var weights = new DisposalAction(() => { order.Add("weights"); throw new InvalidOperationException("second"); });
        var guard = new DisposalAction(() => order.Add("guard"));
        if (suppress) LearningLocalRuntime.DisposeModelResources(context, weights, guard, suppressErrors: true);
        else Assert.Same(first, Assert.Throws<InvalidOperationException>(() => LearningLocalRuntime.DisposeModelResources(context, weights, guard)));
        Assert.Equal(new[] { "context", "weights", "guard" }, order);
    }

    private sealed class DisposalAction(Action action) : IDisposable { public void Dispose() => action(); }

    private sealed class ModelFolder : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("lingrove-gguf-").FullName;
        // These tests exercise header/lease evidence, not filename heuristics. An arbitrary OS
        // temporary basename can accidentally contain a rejected specialist hint such as HyMT.
        internal string ModelPath => Path.Combine(_directory, "renamed.gguf");
        public void Dispose() => Directory.Delete(_directory, recursive: true);
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
