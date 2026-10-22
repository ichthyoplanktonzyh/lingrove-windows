using System.Text.Json;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Support;
using Cotabby.Core.Tests.Suggestions;

namespace Cotabby.Core.Tests.Parity;

/// <summary>
/// Applies the committed macOS-derived JSON examples to Windows' pure suggestion policies. Keeping
/// the corpus outside the test assembly lets its inputs and expected outputs be reviewed or consumed
/// by the C# tests and the repo-local Swift-source verifier without linking to runtime or model code.
/// </summary>
[Collection(SpacelessSegmenter.Collection)]
public sealed class VersionedParityCorpusTests
{
    private static readonly ParityCorpus Corpus = LoadCorpus();

    public static TheoryData<AcceptanceChunkCase> AcceptanceCases => Data(Corpus.AcceptanceChunks);
    public static TheoryData<GhostSpaceBoundaryCase> GhostSpaceCases => Data(Corpus.GhostSpaceBoundaries);
    public static TheoryData<NormalizationCase> NormalizationCases => Data(Corpus.Normalization);

    [Theory]
    [MemberData(nameof(AcceptanceCases))]
    public void NextChunk_matches_macOS_fixture(AcceptanceChunkCase testCase)
    {
        string actual = AcceptanceChunker.NextChunk(
            testCase.Tail,
            autoAcceptTrailingPunctuation: testCase.AutoAcceptTrailingPunctuation);

        Assert.Equal(testCase.ExpectedChunk, actual);
        Assert.Equal(testCase.ExpectedUtf16Length, actual.Length);
    }

    [Theory]
    [MemberData(nameof(GhostSpaceCases))]
    public void LiveAdjusted_matches_macOS_fixture(GhostSpaceBoundaryCase testCase)
    {
        Assert.Equal(
            testCase.Expected,
            GhostSpaceBoundary.LiveAdjusted(
                testCase.Completion,
                testCase.PrecedingText,
                testCase.RequestPrecedingText));
    }

    [Theory]
    [MemberData(nameof(NormalizationCases))]
    public void NormalizeDetailed_matches_macOS_fixture(NormalizationCase testCase)
    {
        if (testCase.PrefixUtf16Length is { } expectedPrefixLength)
        {
            Assert.Equal(expectedPrefixLength, testCase.PrefixText.Length);
        }

        var request = new SuggestionNormalizationRequest(
            testCase.Prompt,
            testCase.PrefixText,
            testCase.PrecedingText,
            testCase.TrailingText,
            testCase.IsMultiLineEnabled);
        CompletionSuppressionReason? expectedSuppression = testCase.ExpectedSuppression is { } reason
            ? Enum.Parse<CompletionSuppressionReason>(reason, ignoreCase: false)
            : null;

        Assert.Equal(
            new SuggestionNormalizationResult(testCase.ExpectedText, expectedSuppression),
            SuggestionTextNormalizer.NormalizeDetailed(testCase.Raw, request));
    }

    private static ParityCorpus LoadCorpus()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "parity", "v1.json");
        using FileStream stream = File.OpenRead(path);
        var corpus = JsonSerializer.Deserialize<ParityCorpus>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidDataException($"The parity fixture at '{path}' is empty.");

        if (corpus.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported parity fixture version {corpus.SchemaVersion}.");
        }
        if (corpus.MacBaselineRevision != "43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f")
        {
            throw new InvalidDataException($"Unexpected macOS fixture baseline '{corpus.MacBaselineRevision}'.");
        }

        ValidateCases(corpus);
        return corpus;
    }

    private static void ValidateCases(ParityCorpus corpus)
    {
        if (corpus.AcceptanceChunks is null
            || corpus.GhostSpaceBoundaries is null
            || corpus.Normalization is null
            || corpus.AcceptanceChunks.Count == 0
            || corpus.GhostSpaceBoundaries.Count == 0
            || corpus.Normalization.Count == 0)
        {
            throw new InvalidDataException("Every parity fixture category must contain at least one case.");
        }

        if (corpus.AcceptanceChunks.Any(testCase => testCase is null)
            || corpus.GhostSpaceBoundaries.Any(testCase => testCase is null)
            || corpus.Normalization.Any(testCase => testCase is null))
        {
            throw new InvalidDataException("Parity fixture categories cannot contain null cases.");
        }

        var metadata = corpus.AcceptanceChunks.Select(testCase => (testCase.Id, testCase.MacTest))
            .Concat(corpus.GhostSpaceBoundaries.Select(testCase => (testCase.Id, testCase.MacTest)))
            .Concat(corpus.Normalization.Select(testCase => (testCase.Id, testCase.MacTest)))
            .ToArray();

        if (metadata.Any(testCase => string.IsNullOrWhiteSpace(testCase.Id)
            || string.IsNullOrWhiteSpace(testCase.MacTest)))
        {
            throw new InvalidDataException("Every parity case must have a non-empty id and macTest provenance.");
        }

        if (metadata.Select(testCase => testCase.Id).Distinct(StringComparer.Ordinal).Count() != metadata.Length)
        {
            throw new InvalidDataException("Parity case ids must be unique across all fixture categories.");
        }
    }

    private static TheoryData<TCase> Data<TCase>(IEnumerable<TCase> cases)
    {
        var data = new TheoryData<TCase>();
        foreach (TCase testCase in cases)
        {
            data.Add(testCase);
        }
        return data;
    }

    public sealed class ParityCorpus
    {
        public required int SchemaVersion { get; init; }
        public required string MacBaselineRevision { get; init; }
        public required List<AcceptanceChunkCase> AcceptanceChunks { get; init; }
        public required List<GhostSpaceBoundaryCase> GhostSpaceBoundaries { get; init; }
        public required List<NormalizationCase> Normalization { get; init; }
    }

    public sealed class AcceptanceChunkCase
    {
        public required string Id { get; init; }
        public required string MacTest { get; init; }
        public required string Tail { get; init; }
        public required bool AutoAcceptTrailingPunctuation { get; init; }
        public required string ExpectedChunk { get; init; }
        public required int ExpectedUtf16Length { get; init; }

        public override string ToString() => Id;
    }

    public sealed class GhostSpaceBoundaryCase
    {
        public required string Id { get; init; }
        public required string MacTest { get; init; }
        public required string Completion { get; init; }
        public required string PrecedingText { get; init; }
        public required string RequestPrecedingText { get; init; }
        public required string Expected { get; init; }

        public override string ToString() => Id;
    }

    public sealed class NormalizationCase
    {
        public required string Id { get; init; }
        public required string MacTest { get; init; }
        public required string Prompt { get; init; }
        public required string PrefixText { get; init; }
        public int? PrefixUtf16Length { get; init; }
        public required string PrecedingText { get; init; }
        public string TrailingText { get; init; } = string.Empty;
        public required string Raw { get; init; }
        public required bool IsMultiLineEnabled { get; init; }
        public required string ExpectedText { get; init; }
        public string? ExpectedSuppression { get; init; }

        public override string ToString() => Id;
    }
}
