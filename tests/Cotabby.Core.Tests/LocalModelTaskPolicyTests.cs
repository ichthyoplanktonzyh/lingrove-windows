using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Models;

namespace Cotabby.Core.Tests.Models;

public class LocalModelTaskPolicyTests
{
    [Fact]
    public void CatalogOwnsEachTaskAndDecodersNeverBecomeCandidates()
    {
        var allTasks = Enum.GetValues<LocalModelTask>().ToHashSet();
        foreach (var entry in LocalModelCatalog.Entries)
        {
            Assert.Equal(entry, LocalModelCatalog.Descriptor(entry.Filename.ToUpperInvariant()));
            foreach (var task in allTasks)
                Assert.Equal(entry.Task == task && !entry.IsResource,
                    LocalModelTaskPolicy.IsCandidate(entry.Filename, task, allTasks));
        }
    }

    [Fact]
    public void UnknownFilesRequireAssignmentAndFamilyRulesSurviveRenaming()
    {
        var none = new HashSet<LocalModelTask>();
        var translation = new HashSet<LocalModelTask> { LocalModelTask.Translation };
        Assert.False(LocalModelTaskPolicy.IsCandidate("custom.gguf", LocalModelTask.Translation, none));
        Assert.True(LocalModelTaskPolicy.IsCandidate("custom.gguf", LocalModelTask.Translation, translation));
        Assert.False(LocalModelTaskPolicy.IsCandidate("custom.gguf", LocalModelTask.Explanation, translation));
        Assert.False(LocalModelTaskPolicy.Supports("Qwen-Base-Q8.gguf", LocalModelTask.Translation));
        Assert.False(LocalModelTaskPolicy.Supports("TranslateGemma-custom.gguf", LocalModelTask.Explanation));
        Assert.False(LocalModelTaskPolicy.Supports("mmproj-custom.gguf", LocalModelTask.Speech));
        Assert.True(LocalModelTaskPolicy.Supports("Qwen3-TTS-other.gguf", LocalModelTask.Speech));
        Assert.False(LocalModelTaskPolicy.Supports("Qwen3-TTS-other.gguf", LocalModelTask.Completion));
        Assert.False(LocalModelTaskPolicy.Supports("", LocalModelTask.Completion));
        foreach (var template in LearningChatTemplatePolicy.AvailableFor(LearningModelRole.Translation))
            Assert.True(LocalModelTaskPolicy.PermitsTemplate(template, LocalModelTask.Translation));
        Assert.False(LocalModelTaskPolicy.PermitsTemplate(LearningChatTemplate.HyMt, LocalModelTask.Explanation));
    }

    [Fact]
    public void FamilyFilteringAndBaseSplittingPreserveCombiningGraphemes()
    {
        // Filtering a letter retains its combining mark; it cannot manufacture an ASCII family.
        Assert.True(LocalModelTaskPolicy.Supports("q\u0301wen3tts.gguf", LocalModelTask.Translation));
        Assert.True(LocalModelTaskPolicy.Supports("qwen3tts\u0301.gguf", LocalModelTask.Translation));
        Assert.True(LocalModelTaskPolicy.Supports("base\u0301-model.gguf", LocalModelTask.Translation));
        Assert.False(LocalModelTaskPolicy.Supports("base-model.gguf", LocalModelTask.Translation));
        Assert.True(LocalModelTaskPolicy.Supports("mmproj\u0301-custom.gguf", LocalModelTask.Translation));
        Assert.False(LocalModelTaskPolicy.Supports("mmproj-custom.gguf", LocalModelTask.Translation));
    }

    [Fact]
    public void InvalidTaskFailsClosedBeforeAssignmentOrTemplateAdmission()
    {
        var invalid = (LocalModelTask)999;
        var assignments = new Dictionary<string, string[]>();
        Assert.False(LocalModelTaskPolicy.Supports("custom.gguf", invalid));
        Assert.False(LocalModelTaskPolicy.IsCandidate("custom.gguf", invalid, new HashSet<LocalModelTask> { invalid }));
        Assert.False(LocalModelTaskPolicy.PermitsTemplate(LearningChatTemplate.Automatic, invalid));
        foreach (var task in Enum.GetValues<LocalModelTask>())
            Assert.False(LocalModelTaskPolicy.PermitsTemplate((LearningChatTemplate)999, task));
        Assert.False(LocalModelTaskAssignments.TryRecord("custom.gguf", invalid, assignments));
        Assert.Empty(assignments);
    }

    [Fact]
    public void ExplicitAssignmentsUseLowercaseKeysSortedTaskValuesAndKeepCatalogOwnership()
    {
        var map = new Dictionary<string, string[]>();
        Assert.True(LocalModelTaskAssignments.TryRecord("CUSTOM.gguf", LocalModelTask.Translation, map));
        Assert.True(LocalModelTaskAssignments.TryRecord("custom.gguf", LocalModelTask.Completion, map));
        Assert.Equal(new[] { "completion", "translation" }, map["custom.gguf"]);
        Assert.Equal(new[] { LocalModelTask.Completion, LocalModelTask.Translation },
            LocalModelTaskAssignments.TasksFor("CUSTOM.gguf", map).Order().ToArray());
        Assert.False(LocalModelTaskAssignments.TryRecord("gemma-4-E2B.i1-Q6_K.gguf", LocalModelTask.Translation, map));
        Assert.True(LocalModelTaskAssignments.TryRecord("mmproj-custom.gguf", LocalModelTask.Speech, map));
        Assert.False(map.ContainsKey("mmproj-custom.gguf"));
        Assert.False(LocalModelTaskPolicy.IsCandidate("mmproj-custom.gguf", LocalModelTask.Speech,
            new HashSet<LocalModelTask> { LocalModelTask.Speech }));
        map["unknown.gguf"] = ["bogus", "speech"];
        Assert.Equal(new[] { LocalModelTask.Speech }, LocalModelTaskAssignments.TasksFor("unknown.gguf", map));
    }

    [Fact]
    public void CompletionCatalogAdmitsOnlyExplicitUnknownsAndCompatibleBuiltIns()
    {
        CotabbyModel Make(string file) => new() { Id = file, DisplayName = file, DownloadUrl = "", FileName = file, ApproxSizeBytes = 1, ContextLength = 4096 };
        var models = new[] { Make("custom.gguf"), Make("TranslateGemma-custom.gguf"), Make("Hy-MT2-1.8B-Q4_K_M.gguf"), ModelCatalog.All[0] };
        var map = new Dictionary<string, string[]> { ["custom.gguf"] = ["completion"], ["translategemma-custom.gguf"] = ["completion"] };
        var candidates = ModelCatalog.CandidatesFrom(models, map);
        Assert.Equal(new[] { "custom.gguf", ModelCatalog.All[0].FileName }, candidates.Select(model => model.FileName));
        map.Clear(); Assert.Equal(new[] { ModelCatalog.All[0].FileName }, ModelCatalog.CandidatesFrom(models, map).Select(model => model.FileName));
    }

    [Fact]
    public void UsageProtectsCurrentAndFutureSelectionsAcrossTasks()
    {
        var names = ModelUsagePolicy.ProtectedFilenames("current.gguf", ["battery.gguf", "", null],
            [new[] { "translation.gguf", "explanation.gguf" }]);
        Assert.True(names.SetEquals(["current.gguf", "battery.gguf", "translation.gguf", "explanation.gguf"]));
    }
}
