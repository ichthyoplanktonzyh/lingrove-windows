using System.Security.Cryptography;
using System.Text;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using F = Cotabby.Core.Vocabulary.Frozen;
using R = Cotabby.Core.Vocabulary.Resources;

namespace Cotabby.Core.Tests.Vocabulary.Resources;

public sealed class FrozenClassificationInstallerTests
{
    [Fact]
    public void CurrentCatalogPreservesSourceBytesAndInstallsGuidance()
    {
        using var stream = typeof(R.VocabularyClassificationCatalog).Assembly.GetManifestResourceStream(R.VocabularyClassificationCatalog.CurrentResourceName)!; using var buffer = new MemoryStream(); stream.CopyTo(buffer); var bytes = buffer.ToArray();
        Assert.Equal("6ce156c0736c8a23373ace9cd56e81a26f24843c", Convert.ToHexString(SHA1.HashData([.. Encoding.ASCII.GetBytes($"blob {bytes.Length}\0"), .. bytes])).ToLowerInvariant());
        var catalog = R.VocabularyClassificationCatalog.CurrentBundled(); Assert.Equal(4, catalog.Version); var (index, installed) = catalog.Install(new(), []); Assert.True(installed); Assert.Contains(index.Categories, x => x.References.Count > 0 && x.Localizations.Count > 0);
        string[] locales = ["en", "zh-Hans", "zh-Hant", "ja", "ko", "fr", "de", "es", "pt-BR", "it", "ru", "ar", "hi", "id", "tr"]; foreach (var category in index.Categories) { Assert.Equal(locales.Order(), category.Localizations.Select(x => x.Language).Order()); Assert.All(category.Localizations, value => { Assert.False(string.IsNullOrEmpty(value.Name)); Assert.False(string.IsNullOrEmpty(value.Definition)); Assert.False(string.IsNullOrEmpty(value.ScopeNote)); }); }
    }
    private static R.VocabularyClassificationCatalog Catalog(long version, bool child = true) => new()
    {
        Key = "test.core", Version = version, Authorship = "Fixture", Categories = child
            ? [new() { Key = "degree", English = "Degree", Chinese = "程度" },
               new() { Key = "near-zero", English = "Near absence", Chinese = "接近于无", Parents = ["degree"] }]
            : [new() { Key = "degree", English = "Degree", Chinese = "程度" }]
    };

    [Fact]
    public void BundledManifestIsExactFrozenGitBlobAndInstallIsIdempotentWithoutInferredMemberships()
    {
        using var stream = typeof(R.VocabularyClassificationCatalog).Assembly.GetManifestResourceStream(R.VocabularyClassificationCatalog.ResourceName)!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); byte[] bytes = buffer.ToArray();
        byte[] prefix = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        Assert.Equal("ee9641a88b970b83e5822b3bf001c2a6b9aa8cb8", Convert.ToHexString(SHA1.HashData([.. prefix, .. bytes])).ToLowerInvariant());
        var empty = new F.VocabularyDocument();
        var disabled = F.VocabularyClassificationInstaller.InstallBundled(empty, false);
        Assert.False(disabled.Installed); Assert.Same(empty, disabled.Document);
        var first = F.VocabularyClassificationInstaller.InstallBundled(empty, true);
        Assert.True(first.Installed); Assert.Empty(empty.Classification.Categories);
        Assert.Equal(256, first.Document.Classification.Categories.Count);
        Assert.Empty(first.Document.Classification.Memberships); Assert.Empty(first.Document.Catalog);
        Assert.Equal(Guid.Parse("0afa17b4-8353-5ff5-b667-9365cf329081"), Assert.Single(first.Document.Classification.Frameworks).Id);
        var again = F.VocabularyClassificationInstaller.InstallBundled(first.Document, true);
        Assert.False(again.Installed); Assert.Same(first.Document, again.Document);
    }

    [Fact]
    public void UpgradeRetainsCanonicalNodesDatesMembershipsLocalOverridesAndRetiredChildren()
    {
        var node = new F.VocabularyMeaningNode(Guid.NewGuid(), [new("Meaning")])
            { CreatedAt = new F.SourceDate(1e20), UpdatedAt = new F.SourceDate(-1e20) };
        var original = new F.VocabularyDocument { Semantics = new() { Nodes = [node] } };
        var initial = F.VocabularyClassificationInstaller.Install(original, Catalog(1)).Document;
        var root = initial.Classification.Categories[0]; var child = initial.Classification.Categories[1];
        var custom = new R.VocabularyCategory(Guid.NewGuid(), R.VocabularyCategoryAxis.Content, [new("Custom")]);
        var edge = new R.VocabularyCategoryParent(Guid.NewGuid(), custom.Id, child.Id);
        var membership = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), node.Id, child.Id);
        var overlay = new R.VocabularyCategoryOverride(root.Id) { Definition = "", IsHidden = true, Labels = [new("Local") { Language = LearningLanguage.English }] };
        var populated = initial with { Classification = initial.Classification with { Categories = initial.Classification.Categories.Append(custom).ToArray(),
            Parents = initial.Classification.Parents.Append(edge).ToArray(), Memberships = [membership], Overrides = [overlay] } };
        var upgraded = F.VocabularyClassificationInstaller.Install(populated, Catalog(2, child: false));
        Assert.True(upgraded.Installed); Assert.Same(populated.Semantics, upgraded.Document.Semantics);
        Assert.Equal(node, Assert.Single(upgraded.Document.Semantics.Nodes)); Assert.Contains(upgraded.Document.Classification.Categories, value => value.Id == child.Id && value.IsRetired);
        var upgradedRoot = Assert.Single(upgraded.Document.Classification.Categories, value => value.Id == root.Id);
        Assert.Equal(root.Labels.Select(label => label.Id), upgradedRoot.Labels.Select(label => label.Id));
        Assert.Equal(initial.Classification.Frameworks[0].Id, upgraded.Document.Classification.Frameworks[0].Id);
        Assert.Contains(custom, upgraded.Document.Classification.Categories); Assert.Contains(edge, upgraded.Document.Classification.Parents);
        Assert.Equal(membership, Assert.Single(upgraded.Document.Classification.Memberships)); Assert.Equal(overlay, Assert.Single(upgraded.Document.Classification.Overrides));
        Assert.False(F.VocabularyClassificationInstaller.Install(upgraded.Document, Catalog(1)).Installed);
        Assert.False(F.VocabularyClassificationInstaller.Install(upgraded.Document, Catalog(2, false)).Installed);
    }

    [Fact]
    public void BadParentOrOrphanMembershipRejectsWholeCandidateWithoutChangingInput()
    {
        var before = new F.VocabularyDocument();
        var invalid = Catalog(1) with { Categories = [new() { Key = "child", English = "Child", Chinese = "子", Parents = ["missing"] }] };
        Assert.Throws<VocabularyException>(() => F.VocabularyClassificationInstaller.Install(before, invalid));
        Assert.Empty(before.Classification.Categories);
        var valid = F.VocabularyClassificationInstaller.Install(before, Catalog(1)).Document;
        var orphan = new R.VocabularyMeaningCategoryMembership(Guid.NewGuid(), Guid.NewGuid(), valid.Classification.Categories[0].Id);
        var corrupted = valid with { Classification = valid.Classification with { Memberships = [orphan] } };
        Assert.Throws<VocabularyException>(() => F.VocabularyClassificationInstaller.Install(corrupted, Catalog(2)));
        Assert.Equal(1, Assert.Single(corrupted.Classification.Frameworks).Version); Assert.Equal(orphan, Assert.Single(corrupted.Classification.Memberships));
    }

    [Fact]
    public void DisabledOwnerNeverLoadsCatalogAndFailedLoadCanBeRetried()
    {
        var document = new F.VocabularyDocument(); int calls = 0;
        R.VocabularyClassificationCatalog Load() { calls++; if (calls == 1) throw new IOException("fixture"); return Catalog(1); }
        var disabled = F.VocabularyClassificationInstaller.Install(document, false, Load);
        Assert.False(disabled.Installed); Assert.Same(document, disabled.Document); Assert.Equal(0, calls);
        Assert.Throws<IOException>(() => F.VocabularyClassificationInstaller.Install(document, true, Load));
        Assert.Empty(document.Classification.Categories);
        var retry = F.VocabularyClassificationInstaller.Install(document, true, Load);
        Assert.True(retry.Installed); Assert.Equal(2, calls); Assert.Equal(2, retry.Document.Classification.Categories.Count);
    }
}
