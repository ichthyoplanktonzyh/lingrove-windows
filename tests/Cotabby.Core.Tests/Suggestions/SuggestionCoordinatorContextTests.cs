using Cotabby.Core.Suggestions;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class SuggestionCoordinatorContextTests : CoordinatorHarness
{
    private const string Agenda = "meeting agenda for Thursday";

    private readonly FakeClipboard _clipboard;

    public SuggestionCoordinatorContextTests() : this(new FakeClipboard(Agenda))
    {
    }

    private SuggestionCoordinatorContextTests(FakeClipboard clipboard) : base(clipboard: clipboard)
    {
        _clipboard = clipboard;
        Engine.Completion = _ => " starts soon";
        // These cases assert context on a new request. Predict-ahead may instead reuse and
        // rebase the first in-flight request; streaming/rebasing has its own coordinator suite.
        Coordinator.Settings = SuggestionSettings.Default with { PredictAheadWhileTyping = false };
    }

    private async Task<SuggestionRequest> RequestFor(string prefix)
    {
        SuggestionRequest? Find() => Engine.Requests.LastOrDefault(request => request.Prefix == prefix);
        await Eventually(() => Find() is not null, $"a request for \"{prefix}\" is sent");
        return Find()!;
    }

    private void EnableClipboardContext() =>
        Coordinator.Settings = Coordinator.Settings with { ClipboardContext = true };

    // Match the real hook's UI owner: observe each key before its host edit, without an
    // independent test thread letting the pump process a batch of already-applied host edits.
    private Task TypeOnUiAsync(string text) => RunOnUiAsync(() => Type(text));

    [Fact]
    public async Task Clipboard_AFreshRelevantCopyReachesTheRequest()
    {
        EnableClipboardContext();
        await TypeOnUiAsync("the meeting");
        await RequestFor("the meeting");

        _clipboard.Copy($"  {Agenda}\n");
        await TypeOnUiAsync(" now");

        Assert.Equal(Agenda, (await RequestFor("the meeting now")).ClipboardContext);
    }

    [Fact]
    public async Task Clipboard_AFreshCopyUnrelatedToTheTextStaysOut()
    {
        EnableClipboardContext();
        await TypeOnUiAsync("the meeting");
        await RequestFor("the meeting");

        _clipboard.Copy("SELECT * FROM users");
        await TypeOnUiAsync(" now");

        Assert.Equal("", (await RequestFor("the meeting now")).ClipboardContext);
    }

    [Fact]
    public async Task Clipboard_TextCopiedBeforeStartupStaysOut()
    {
        EnableClipboardContext();
        await TypeOnUiAsync("the meeting");
        await RequestFor("the meeting");
        await TypeOnUiAsync(" now");
        await RequestFor("the meeting now");

        Assert.All(Engine.Requests, request => Assert.Equal("", request.ClipboardContext));
    }

    [Fact]
    public async Task Clipboard_IsNeverReadWhileTheSettingIsOff()
    {
        await TypeOnUiAsync("the meeting");
        await RequestFor("the meeting");

        _clipboard.Copy(Agenda);
        await TypeOnUiAsync(" now");

        Assert.Equal("", (await RequestFor("the meeting now")).ClipboardContext);
        Assert.Equal(0, _clipboard.Reads);
    }

    [Fact]
    public async Task Clipboard_AnAcceptedCopyIsNotReadAgainForTheSameHost()
    {
        EnableClipboardContext();
        await TypeOnUiAsync("the meeting");
        await RequestFor("the meeting");
        _clipboard.Copy(Agenda);
        await TypeOnUiAsync(" now");
        await RequestFor("the meeting now");
        int reads = _clipboard.Reads;

        await TypeOnUiAsync("!");

        Assert.Equal(Agenda, (await RequestFor("the meeting now!")).ClipboardContext);
        Assert.Equal(reads, _clipboard.Reads);
    }

    [Fact]
    public async Task Surface_AChatAppIsDescribed()
    {
        Focus.Focus(Fields.Text("", process: "slack") with { WindowTitle = "design - Slack", HelpText = "Message #design" });

        await TypeOnUiAsync("sounds good");

        Assert.Equal(
            ["Format: chat; App: slack; Title: design; Field: Message #design."],
            (await RequestFor("sounds good")).SurfaceLines);
    }

    [Fact]
    public async Task Surface_ACodeEditorIsNot()
    {
        Focus.Focus(Fields.Text("", process: "Code") with { WindowTitle = "Program.cs - Visual Studio Code" });

        await TypeOnUiAsync("returns the");

        Assert.Empty((await RequestFor("returns the")).SurfaceLines);
    }
}
