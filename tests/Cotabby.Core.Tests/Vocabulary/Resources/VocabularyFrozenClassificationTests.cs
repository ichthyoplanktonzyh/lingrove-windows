using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class VocabularyFrozenClassificationTests
{
    private static R.VocabularyCategory Category(string name, R.VocabularyCategoryAxis axis = R.VocabularyCategoryAxis.Content)
        => new(Guid.NewGuid(), axis, [new(name) { Language = LearningLanguage.English }]);
    private static R.VocabularyClassificationCatalog Catalog(int version = 1, bool child = true) => new()
    {
        Key = "test.core", Version = version, Authorship = "Fixture", Categories = child
            ? [new() { Key = "degree", English = "Degree", Chinese = "程度" }, new() { Key = "near-zero", English = "Near absence", Chinese = "接近于无", Parents = ["degree"] }]
            : [new() { Key = "degree", English = "Degree", Chinese = "程度" }]
    };
    private const string Manifest = """
        {"key":"test.core","version":1,"authorship":"Fixture",
         "references":[{"title":"Reference","url":"https://example.test","role":"Fixture"}],
         "categories":[{"key":"root","axis":"content","english":"Root","chinese":"根","definition":"","parents":[]}]}
        """;

    [Theory]
    [InlineData("\"references\":[", "\"References\":[")]
    [InlineData("\"categories\":[", "\"Categories\":[")]
    [InlineData("\"axis\":\"content\"", "\"Axis\":\"content\"")]
    [InlineData("\"parents\":[]", "\"Parents\":[]")]
    [InlineData("\"url\":", "\"URL\":")]
    [InlineData("\"references\":[{\"title\":\"Reference\",\"url\":\"https://example.test\",\"role\":\"Fixture\"}]", "\"references\":null")]
    [InlineData("\"categories\":[{\"key\":\"root\",\"axis\":\"content\",\"english\":\"Root\",\"chinese\":\"根\",\"definition\":\"\",\"parents\":[]}]", "\"categories\":null")]
    [InlineData("\"references\":[{\"title\":\"Reference\",\"url\":\"https://example.test\",\"role\":\"Fixture\"}]", "\"references\":[null]")]
    [InlineData("\"parents\":[]", "\"parents\":[null]")]
    [InlineData("\"axis\":\"content\"", "\"axis\":null")]
    [InlineData("\"parents\":[]", "\"parents\":null")]
    [InlineData("\"axis\":\"content\"", "\"axis\":\"Content\"")]
    [InlineData("\"axis\":\"content\"", "\"axis\":\"unknown\"")]
    [InlineData("\"axis\":\"content\"", "\"axis\":0")]
    [InlineData(",\"parents\":[]", "")]
    [InlineData(",\"definition\":\"\"", "")]
    public void CatalogDecoderRejectsNonSwiftKeysNullRequiredArraysAndInvalidAxis(string original, string replacement)
    {
        Assert.Contains(original, Manifest);
        var bytes = System.Text.Encoding.UTF8.GetBytes(Manifest.Replace(original, replacement, StringComparison.Ordinal));
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationCatalog.Decode(bytes));
    }

    [Theory]
    [InlineData("1.0", 1L)]
    [InlineData("2147483648", 2147483648L)]
    [InlineData("2147483648.0", 2147483648L)]
    public void CatalogVersionRetainsSwiftInt64AndFoundationIntegralNumberSpellings(string spelling, long expected)
    {
        string json = "{\"key\":\"test\",\"version\":" + spelling + ",\"authorship\":\"\",\"references\":[],\"categories\":[]}";
        var catalog = R.VocabularyClassificationCatalog.Decode(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(expected, catalog.Version);
        var (index, installed) = catalog.Install(new(), []); Assert.True(installed);
        Assert.Equal(expected, Assert.Single(index.Frameworks).Version);
        Assert.False(catalog.Install(index, []).Installed);
    }
    [Fact]
    public void CatalogDecoderBoundsMalformedBytesAndDepthButAcceptsEmptyRequiredArrays()
    {
        Assert.Single(R.VocabularyClassificationCatalog.Decode(System.Text.Encoding.UTF8.GetBytes(Manifest)).Categories);
        var empty = """{"key":"empty","version":1,"authorship":"","references":[],"categories":[]}""";
        var catalog = R.VocabularyClassificationCatalog.Decode(System.Text.Encoding.UTF8.GetBytes(empty));
        Assert.Empty(catalog.References); Assert.Empty(catalog.Categories);
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationCatalog.Decode([0xff]));
        byte[] badUtf8 = [.. "{\"key\":\""u8, 0xff, .. "\",\"version\":1,\"authorship\":\"\",\"references\":[],\"categories\":[]}"u8];
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationCatalog.Decode(badUtf8));
        var deep = Manifest.Replace("\"key\":\"test.core\"", "\"extra\":" + new string('[', 33) + "0" + new string(']', 33) + ",\"key\":\"test.core\"", StringComparison.Ordinal);
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationCatalog.Decode(System.Text.Encoding.UTF8.GetBytes(deep)));
    }
    [Fact]
    public void BundledCatalogRetainsStableMultilingualKeysAndAxes()
    {
        Assert.Equal(Guid.Parse("0afa17b4-8353-5ff5-b667-9365cf329081"), R.VocabularyClassificationCatalog.Identifier("lingrove.core"));
        var catalog = R.VocabularyClassificationCatalog.Bundled(); var (index, installed) = catalog.Install(new(), []);
        Assert.True(installed); Assert.Equal(256, index.Categories.Count);
        Assert.Equal(20, catalog.Categories.Count(x => x.Parents.Count == 0 && x.Axis == R.VocabularyCategoryAxis.Content));
        Assert.Equal(6, catalog.Categories.Count(x => x.Parents.Count == 0 && x.Axis == R.VocabularyCategoryAxis.Function));
        var category = Assert.Single(index.Categories, x => x.Key == "content.degree.near-zero");
        Assert.Equal("接近于无", R.VocabularyClassificationPolicy.Title(category, index, [LearningLanguage.Chinese]));
        Assert.Equal("Near absence", R.VocabularyClassificationPolicy.Title(category, index, [LearningLanguage.English]));
        Assert.False(catalog.Install(index, []).Installed);
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationCatalog.Decode(new byte[R.VocabularyClassificationCatalog.MaximumCatalogBytes + 1]));
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationCatalog.Decode("{}"u8));
    }
    [Fact]
    public void UpgradeKeepsRetiredIdentitiesExtensionsMembershipsAndOverlaysAndCannotDowngrade()
    {
        var node = new R.VocabularyMeaningNode(Guid.NewGuid(), [new("Meaning")]);
        var (initial, installed) = Catalog().Install(new(), [node]); Assert.True(installed);
        var root = initial.Categories[0]; var child = initial.Categories[1]; var custom = Category("My category");
        var membership = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, child.Id);
        var overlay = new R.VocabularyCategoryOverride(root.Id) { Labels = [new("My degree")], Definition = "", IsHidden = true };
        var extended = initial with { Categories = initial.Categories.Append(custom).ToArray(), Parents = initial.Parents.Append(new(Guid.NewGuid(), custom.Id, child.Id)).ToArray(),
            Memberships = [membership], Overrides = [overlay] };
        var (upgraded, changed) = Catalog(2, false).Install(extended, [node]); Assert.True(changed);
        Assert.Contains(upgraded.Categories, x => x.Id == child.Id && x.IsRetired); Assert.Contains(custom, upgraded.Categories);
        Assert.Equal(membership, Assert.Single(upgraded.Memberships)); Assert.Equal(overlay, Assert.Single(upgraded.Overrides));
        Assert.Equal("", R.VocabularyClassificationPolicy.Effective(upgraded.Categories.First(x => x.Id == root.Id), upgraded).Definition);
        Assert.False(Catalog().Install(upgraded, [node]).Installed); Assert.Equal(2, Assert.Single(upgraded.Frameworks).Version);
    }
    [Fact]
    public void MultipleParentsDeduplicateMeaningsAndRejectCyclesCrossAxesAndOrphans()
    {
        var root = Category("Root"); var left = Category("Left"); var right = Category("Right"); var child = Category("Child");
        var node = new R.VocabularyMeaningNode(Guid.NewGuid(), [new("Meaning")]);
        var graph = new R.VocabularyClassificationIndex { Categories = [root,left,right,child], Parents = [new(Guid.NewGuid(),left.Id,root.Id), new(Guid.NewGuid(),right.Id,root.Id),
            new(Guid.NewGuid(),child.Id,left.Id), new(Guid.NewGuid(),child.Id,right.Id)], Memberships = [new(Guid.NewGuid(),node.Id,child.Id),new(Guid.NewGuid(),node.Id,left.Id)] };
        R.VocabularyClassificationPolicy.Validate(graph, [node]); Assert.Equal(node.Id, Assert.Single(R.VocabularyClassificationPolicy.MeaningIds(root.Id, graph)));
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationPolicy.Validate(graph with { Parents = graph.Parents.Append(new(Guid.NewGuid(),root.Id,child.Id)).ToArray() }, [node]));
        var function = Category("Function", R.VocabularyCategoryAxis.Function);
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationPolicy.Validate(new() { Categories = [root,function], Parents = [new(Guid.NewGuid(),function.Id,root.Id)] }, []));
        Assert.Throws<VocabularyException>(() => R.VocabularyClassificationPolicy.Validate(graph with { Overrides = [new(Guid.NewGuid())] }, [node]));
    }
    [Fact]
    public void SelectionRetainsAncestorsAndCustomDeletionDoesNotDeleteMeaning()
    {
        var node = new R.VocabularyMeaningNode(Guid.NewGuid(), [new("Meaning")]); var (index, _) = Catalog().Install(new(), [node]);
        var custom = Category("custom"); index = index with { Categories = index.Categories.Append(custom).ToArray(), Memberships = [new(Guid.NewGuid(),node.Id,index.Categories[1].Id)] };
        var selected = R.VocabularyClassificationPolicy.Selected(index, [node]); Assert.Equal(2, selected.Categories.Count); Assert.DoesNotContain(custom, selected.Categories);
        var removed = R.VocabularyClassificationPolicy.RemoveCategory(custom.Id, index); Assert.Equal(index.Memberships, removed.Memberships);
        R.VocabularyClassificationPolicy.Validate(selected, [node]);
    }
    [Fact]
    public void DetachedEditsPreserveIndependentChangesAndRejectConflicts()
    {
        var first = Category("first"); var second = Category("second"); var baseline = new R.VocabularyClassificationIndex { Categories = [first] };
        var edited = baseline with { Categories = [first with { Definition = "draft" }] }; var current = baseline with { Categories = [first, second] };
        var result = R.VocabularyClassificationPolicy.Merge(edited, baseline, current); Assert.Equal("draft", result.Categories[0].Definition); Assert.Contains(second, result.Categories);
        var error = Assert.Throws<VocabularyException>(() => R.VocabularyClassificationPolicy.Merge(edited, baseline, baseline with { Categories = [first with { Definition = "other" }] }));
        Assert.Equal(VocabularyFailure.EditConflict, error.Failure);
    }
    [Fact]
    public void CopiesListsAndUpgradePreservesV7EvidenceWithoutRelabeling()
    {
        var labels = new List<R.VocabularyMeaningLabel> { new("Name") }; var category = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, labels);
        labels.Clear(); Assert.Single(category.Labels);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var evidence = new R.VocabularySourceSnapshot(Guid.NewGuid(), "Uncollected evidence", R.VocabularySourceKind.Imported, null, now);
        var source = new R.VocabularyDocument { Sources = [evidence] }; var upgraded = R.VocabularyFrozenDocument.Upgrade(source);
        Assert.Same(source, upgraded.Resources); Assert.Equal(evidence, Assert.Single(upgraded.Resources.Sources)); Assert.Equal(7, upgraded.Resources.SchemaVersion); Assert.Equal(8, R.VocabularyFrozenDocument.CurrentVersion); Assert.Empty(upgraded.Classification.Categories);
        Assert.Throws<VocabularyException>(() => R.VocabularyFrozenDocument.Upgrade(source with { SchemaVersion = 99 }));
    }
}
