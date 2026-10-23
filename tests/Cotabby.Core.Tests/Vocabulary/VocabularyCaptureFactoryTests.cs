using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;

namespace Cotabby.Core.Tests.Vocabulary;

public sealed class VocabularyCaptureFactoryTests
{
    private static LearningPreviewState Ready => new()
    {
        RequestId = "fixed", Status = LearningPreviewStatus.Ready, Source = "现在走。", Result = "Go now.",
        Options = new() { SourceLanguage = LearningLanguage.Chinese, TargetLanguage = LearningLanguage.English },
    };
    private sealed class Clock : TimeProvider
    {
        public int Calls { get; private set; }
        public DateTimeOffset Now { get; } = DateTimeOffset.UnixEpoch.AddTicks(1234567);
        public override DateTimeOffset GetUtcNow() { Calls++; return Now; }
    }
    private sealed class Facts : ILearningLexicalText, ILearningEvidenceSentenceTokenizer
    {
        public IReadOnlyList<LearningTextRange> Tokenize(string value, LearningLanguage language) => [new(0, value.Length)];
        public IReadOnlyList<LearningDictionaryWord> Words(string value, LearningLanguage language) => value == "Go"
            ? [new("Go", new(0, 2))] : [new("Go", new(0, 2)), new("now", new(3, 3))];
        public string Key(string value) => throw new NotSupportedException();
        public bool IsPatternText(string value) => throw new NotSupportedException();
        public bool FirstIsUppercase(string value) => throw new NotSupportedException();
    }
    [Fact]
    public void NewEncounterOwnsIdentityClockAndFrozenApplicationProvenance()
    {
        var facts = new Facts(); var clock = new Clock(); var factory = new VocabularyCaptureFactory(facts, facts, clock);
        var first = factory.FromWritingSentencePreview(Ready, "writer")!; var second = factory.FromWritingSentencePreview(Ready, "writer")!;
        Assert.NotNull(first.Example); Assert.NotEqual(Guid.Empty, first.Example.Id); Assert.NotEqual(first.Example.Id, second.Example!.Id);
        Assert.Equal(clock.Now, first.Example.CreatedAt); Assert.Equal(2, clock.Calls); Assert.Equal("writer", first.Example.SourceApplicationId);
        Assert.Equal(Ready.Source, first.Example.Translation); Assert.Equal(LearningLanguage.Chinese, first.Example.TranslationLanguage);
        VocabularyCapturePolicy.Validate(first);
        var draft = VocabularyPolicy.Save(first, new(), clock.Now).Document; VocabularyArchiveValidation.Validate(draft);
    }
    [Fact]
    public void ComponentRetainsEncounterIdentityAndTranslationWhileClearingContextualMeaning()
    {
        var facts = new Facts(); var clock = new Clock(); var factory = new VocabularyCaptureFactory(facts, facts, clock);
        var word = new LearningDictionaryWord("now", new(3, 3));
        var unit = new LearningUnit(1, "Go now", "go now", LearningUnitKind.Expression, [new(0, 6)], [new("Go", new(0, 2)), word]);
        var parent = factory.FromUnit(unit, Ready, "writer")!; var component = VocabularyCaptureFactory.FromComponent(word, unit, parent)!;
        Assert.NotNull(parent.Example); Assert.NotNull(component.Example);
        Assert.Equal(parent.Example.Id, component.Example.Id); Assert.Equal(parent.Example.CreatedAt, component.Example.CreatedAt); Assert.Equal(1, clock.Calls);
        Assert.Equal(parent.Example.SourceApplicationId, component.Example.SourceApplicationId); Assert.Equal(parent.Example.Translation, component.Example.Translation);
        Assert.Equal(parent.Example.TranslationLanguage, component.Example.TranslationLanguage); Assert.Equal(new VocabularyTextRange(3, 3), Assert.Single(component.Example.Ranges));
        Assert.Equal("", component.Example.Meaning); Assert.Null(component.Example.MeaningLanguage); Assert.Null(component.MeaningLanguage);
        Assert.Equal(VocabularyEntryKind.Word, component.Kind); VocabularyCapturePolicy.Validate(component);
    }
    [Fact]
    public void StandaloneWordDoesNotInventAnExampleOrReadTheClock()
    {
        var facts = new Facts(); var clock = new Clock(); var capture = new VocabularyCaptureFactory(facts, facts, clock).FromWritingSentencePreview(Ready with { Result = "Go" });
        Assert.NotNull(capture); Assert.Null(capture.Example); Assert.Equal(0, clock.Calls); Assert.Equal(VocabularyEntryKind.Word, capture.Kind);
        Assert.Equal(Ready.Source, capture.Meaning); Assert.Equal(LearningLanguage.Chinese, capture.MeaningLanguage);
    }
}
