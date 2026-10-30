using System.Net;
using System.Text.Json;
using Cotabby.Core.LanguageLearning;
using Cotabby.Core.Vocabulary;
using Cotabby.Inference.LanguageLearning;
using Cotabby.Inference.OpenAICompatible;

namespace Cotabby.Inference.Tests.LanguageLearning;

public sealed class LearningEndpointInstructionEngineTests
{
    private static LearningModelSnapshot Profile => new() { Backend = LearningModelBackend.Endpoint,
        BaseUrl = "https://explanation.invalid/v1", EndpointModel = "general-instruction",
        SamplingOverrides = new(Temperature: .42, TopP: .73, TopK: 17, SeedMode: LearningModelSeedMode.Fixed, FixedSeed: 123) };
    private static LearningExplanationInput Input => new("know", "know", LearningLanguage.English,
        VocabularyEntryKind.Word, "Let me know.", [new(7, 4)], LearningExplanationContext.Sentence, "owned-test");
    private static HttpResponseMessage Completed(string raw, string finish = "stop") => StubHttpMessageHandler.Json(
        JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = raw }, finish_reason = finish } } }));
    private sealed class Keys(Action? read = null) : IOpenAICompatibleCredentialStore
    {
        internal int Reads;
        public string? ReadApiKey() { Reads++; read?.Invoke(); return "owned-secret"; }
        public void SaveApiKey(string? value) => throw new InvalidOperationException();
        public void DeleteApiKey() => throw new InvalidOperationException();
    }

    [Fact]
    public async Task CompleteExplanationUsesItsOwnPromptDestinationAnd24kRawCap()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        string explanation = new('x', 13_000);
        handler.Respond(_ => Completed(JsonSerializer.Serialize(new { meaning = "告知", explanation })));
        var keys = new Keys(); await using var engine = new LearningEndpointExplanationEngine(Profile, keys, http);
        Assert.Empty(handler.Requests); Assert.Equal(0, keys.Reads);
        var result = await engine.GenerateAsync(Input, LearningLanguage.Chinese, 1024, default);
        Assert.Equal(explanation, result.Explanation); Assert.Equal("告知", result.Meaning);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal("https://explanation.invalid/v1/chat/completions", sent.Url);
        Assert.Equal("Bearer owned-secret", sent.Authorization);
        Assert.Equal("general-instruction", sent.Json.GetProperty("model").GetString());
        Assert.Equal(LearningExplanationPolicy.CurrentPrompt(Input, LearningLanguage.Chinese, 8192, 1024),
            sent.Json.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal(.42, sent.Json.GetProperty("temperature").GetDouble());
        Assert.Equal(.73, sent.Json.GetProperty("top_p").GetDouble());
        Assert.Equal(1024, sent.Json.GetProperty("max_tokens").GetInt32());
        Assert.False(sent.Json.TryGetProperty("top_k", out _)); Assert.False(sent.Json.TryGetProperty("seed", out _));
        Assert.Equal(1, keys.Reads);
    }

    [Fact]
    public async Task RetiredPermitAfterCredentialReadStopsAtActualTransport()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        bool allowed = true; var keys = new Keys(() => allowed = false);
        await using var engine = new LearningEndpointInstructionEngine(Profile, keys, http);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GenerateAsync("owned prompt", 512, 12000, default, () => allowed));
        Assert.Equal(1, keys.Reads); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LocalProfileNeverReadsAnEndpointKeyOrSends()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        var keys = new Keys(); await using var engine = new LearningEndpointInstructionEngine(new(), keys, http);
        Assert.Equal(LearningTranslationEndpointFailure.NotConfigured,
            (await Assert.ThrowsAsync<LearningTranslationEndpointException>(() => engine.GenerateAsync("owned", 512, 12000, default))).EndpointFailure);
        Assert.Equal(0, keys.Reads); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CredentialFailureExposesOnlyAFixedError()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        var keys = new Keys(() => throw new IOException("private credential data"));
        await using var engine = new LearningEndpointInstructionEngine(Profile, keys, http);
        var error = await Assert.ThrowsAsync<LearningTranslationEndpointException>(() => engine.GenerateAsync("owned", 512, 12000, default));
        Assert.Equal(LearningTranslationEndpointFailure.CredentialUnavailable, error.EndpointFailure);
        Assert.DoesNotContain("private credential data", error.ToString()); Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("length", LearningTranslationEndpointFailure.IncompleteResponse)]
    [InlineData("stop", LearningTranslationEndpointFailure.ResponseTooLarge)]
    public async Task IncompleteOrOversizedOutputNeverBecomesACompleteAnswer(string finish, LearningTranslationEndpointFailure expected)
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        handler.Respond(_ => Completed(new string('x', 12001), finish));
        await using var engine = new LearningEndpointInstructionEngine(Profile, new Keys(), http);
        Assert.Equal(expected, (await Assert.ThrowsAsync<LearningTranslationEndpointException>(() => engine.GenerateAsync("owned", 512, 12000, default))).EndpointFailure);
    }

    [Fact]
    public async Task InvalidCaptureAndImpossibleUnicodePrecedeCredentialAccess()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false); var keys = new Keys();
        await using (var engine = new LearningEndpointExplanationEngine(Profile, keys, http))
            await Assert.ThrowsAsync<LearningExplanationException>(() => engine.GenerateAsync(new LearningExplanationInput("", "", LearningLanguage.English, VocabularyEntryKind.Word, "", [], LearningExplanationContext.None), LearningLanguage.English, 1024, default));
        await using (var engine = new LearningEndpointInstructionEngine(Profile, keys, http))
            await Assert.ThrowsAsync<LearningTranslationEndpointException>(() => engine.GenerateAsync("\ud800", 512, 12000, default));
        Assert.Equal(0, keys.Reads); Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RedirectIsRejectedWithoutAnotherRequest()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.TemporaryRedirect));
        await using var engine = new LearningEndpointInstructionEngine(Profile, new Keys(), http);
        Assert.Equal(LearningTranslationEndpointFailure.RedirectRejected,
            (await Assert.ThrowsAsync<LearningTranslationEndpointException>(() => engine.GenerateAsync("owned", 512, 12000, default))).EndpointFailure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DisposalJoinsAnAlreadyAcceptedIgnoringCancellationResponse()
    {
        using var handler = new StubHttpMessageHandler(); using var http = new HttpClient(handler, false);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Responder = async (_, _) => { entered.TrySetResult(); await release.Task; return Completed("owned"); };
        var engine = new LearningEndpointInstructionEngine(Profile, new Keys(), http);
        var generating = engine.GenerateAsync("owned", 512, 12000, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var retiring = engine.DisposeAsync().AsTask(); Assert.False(retiring.IsCompleted);
            release.TrySetResult(); await retiring.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generating);
        }
        finally { release.TrySetResult(); await engine.DisposeAsync(); }
        handler.Respond(_ => Completed("still caller owned"));
        using var response = await http.GetAsync("https://owned.invalid"); Assert.True(response.IsSuccessStatusCode);
    }
}
