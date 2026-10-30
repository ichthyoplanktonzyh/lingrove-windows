using Cotabby.Core.LanguageLearning;

namespace Cotabby.Core.Tests.LanguageLearning;

public sealed class DictionaryWebsitePolicyTests
{
    [Fact]
    public void QueryIsOneEscapedComponentAndCannotChooseAuthority()
    {
        var value = DictionaryWebsitePolicy.Url("https://example.org/search?q={query}", "中 /?#&");
        Assert.NotNull(value); Assert.Equal("example.org", value.Host);
        Assert.EndsWith("q=%E4%B8%AD%20%2F%3F%23%26", value.AbsoluteUri);
        Assert.Null(DictionaryWebsitePolicy.Url("https://{query}.example.org/", "example"));
        Assert.Null(DictionaryWebsitePolicy.Url("https://user:secret@example.org/{query}", "example"));
        Assert.Null(DictionaryWebsitePolicy.Url("http://example.org/{query}", "example"));
        Assert.Null(DictionaryWebsitePolicy.Url("https://example.org/{query}/{query}", "example"));
        Assert.Null(DictionaryWebsitePolicy.Url("https://example.org/", "example"));
        Assert.Null(DictionaryWebsitePolicy.Url("https://example.org/{query}", ""));
        Assert.Null(DictionaryWebsitePolicy.Url("https://example.org/{query}" + new string('a', 2048), "example"));
    }
    [Fact]
    public void OnlineDecodeRetainsSensesExamplesAndAttributionWithoutAudio()
    {
        byte[] json = System.Text.Encoding.UTF8.GetBytes("""
            [{"word":"test","phonetics":[{"text":"/tɛst/","audio":"https://audio.invalid/x"},{"text":"/tɛst/"}],
              "meanings":[{"partOfSpeech":"noun","definitions":[{"definition":"trial","example":"a test"}]}],
              "sourceUrls":["javascript:bad","https://source.example/word"],"license":{"name":"CC","url":"https://license.example/"}}]
            """);
        var values = Assert.IsType<LearningDictionaryResult.Entries>(OnlineDictionaryPolicy.Decode(json)).Values;
        var entry = Assert.Single(values); Assert.Equal("online-0-test", entry.Id);
        Assert.Equal("trial", Assert.Single(Assert.Single(entry.Senses).Definitions));
        Assert.Equal("a test", Assert.Single(entry.Examples!)); Assert.Equal("/tɛst/", Assert.Single(entry.Phonetics!));
        Assert.Equal("https://source.example/word", entry.SourceUrl); Assert.Equal("CC", entry.LicenseName);
        Assert.Throws<InvalidDataException>(() => OnlineDictionaryPolicy.Decode(new byte[1_000_001]));
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[{\"word\":null,\"meanings\":[]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":null}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[null]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[{\"partOfSpeech\":null,\"definitions\":[]}]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":null}]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[null]}]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[{\"partOfSpeech\":\"noun\",\"definitions\":[{\"definition\":null}]}]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[],\"sourceUrls\":[null]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[],\"phonetics\":[null]}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[],\"license\":{\"name\":null,\"url\":\"\"}}]")]
    [InlineData("[{\"word\":\"cat\",\"meanings\":[],\"license\":{\"name\":\"\",\"url\":null}}]")]
    public void OnlineDecodeRejectsNullRequiredValuesLikeSwift(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => OnlineDictionaryPolicy.Decode(System.Text.Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void DecoderValidatesBeyondVisiblePrefixButRetainsPermittedEmptyAndOptionalValues()
    {
        const string valid = "{\"word\":\"\",\"meanings\":[{\"partOfSpeech\":\"\",\"definitions\":[{\"definition\":\"\",\"example\":null}]}],\"phonetics\":[{\"text\":null}]}";
        var bytes = System.Text.Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat(valid, 17)) + "]");
        var values = Assert.IsType<LearningDictionaryResult.Entries>(OnlineDictionaryPolicy.Decode(bytes)).Values;
        Assert.Equal(16, values.Count);
        Assert.Equal("", values[0].Headword);
        Assert.Equal("", Assert.Single(Assert.Single(values[0].Senses).Definitions));
        Assert.Empty(values[0].Phonetics!);
        Assert.Empty(values[0].Examples!);
        var invalid = System.Text.Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat(valid, 16)) + ",null]");
        Assert.Throws<System.Text.Json.JsonException>(() => OnlineDictionaryPolicy.Decode(invalid));
    }
}
