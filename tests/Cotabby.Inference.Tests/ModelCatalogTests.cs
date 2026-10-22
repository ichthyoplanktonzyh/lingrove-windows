using Cotabby.Core.Models;

namespace Cotabby.Inference.Tests;

[Collection(ModelDirectoryCollection.Name)]
public sealed class ModelCatalogTests : IDisposable
{
    private readonly ModelDirectory _models = new();

    public void Dispose() => _models.Dispose();

    [Fact]
    public void Available_ListsBuiltInModelsWhenTheFolderIsMissing() =>
        Assert.Equal(ModelCatalog.All, ModelCatalog.Available());

    [Fact]
    public void Available_AddsEveryOtherGgufInTheFolder()
    {
        _models.Write("my-model.Q4.gguf");
        _models.Write(ModelCatalog.All[0].FileName);
        _models.Write("notes.txt");

        var custom = ModelCatalog.Available().Where(m => m.IsCustom).ToList();

        var model = Assert.Single(custom);
        Assert.Equal("my-model.Q4", model.DisplayName);
        Assert.Equal("my-model.Q4.gguf", model.FileName);
        Assert.Equal(model, ModelCatalog.FindById(model.Id));
    }

    [Theory]
    [InlineData("Hy-MT2-1.8B-Q4_K_M.gguf")]
    [InlineData("Hy-MT2-7B-Q4_K_M.gguf")]
    [InlineData("translategemma-4b-it-Q4_K_M.gguf")]
    [InlineData("Gemma-4-E4B-Q4_K_M.gguf")]
    [InlineData("Qwen3.5-4B-Q4_K_M.gguf")]
    public void Available_ExcludesExactLearningCatalogModelsButKeepsOtherCustomModels(string learningModel)
    {
        _models.Write(learningModel);
        _models.Write("my-custom-model.gguf");
        _models.Write("my-translategemma-copy.gguf");

        var available = ModelCatalog.Available();
        var customFileNames = available.Where(model => model.IsCustom).Select(model => model.FileName).ToList();

        Assert.Equal(ModelCatalog.All, available.Where(model => !model.IsCustom));
        Assert.DoesNotContain(learningModel, customFileNames, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("my-custom-model.gguf", customFileNames);
        // The Mac filter excludes exact catalog filenames; renamed custom files stay selectable.
        Assert.Contains("my-translategemma-copy.gguf", customFileNames);
    }

    [Fact]
    public void FindById_ForgetsACustomModelThatWasRemoved()
    {
        _models.Write("gone.gguf");
        var id = ModelCatalog.Available().Single(m => m.IsCustom).Id;

        File.Delete(Path.Combine(_models.Path, "gone.gguf"));

        Assert.Null(ModelCatalog.FindById(id));
    }
}
