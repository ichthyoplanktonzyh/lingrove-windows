using System.Reflection;
using Cotabby.Core.Focus;
using Cotabby.Core.Input;
using Cotabby.Core.Spelling;
using Cotabby.Core.Suggestions;
using Cotabby.Core.Tests.Spelling;

namespace Cotabby.Core.Tests.Suggestions;

public sealed class SuggestionCoordinatorSafetyTests : CoordinatorHarness
{
    private const string Dictionary = "the 1000\nhello 900\nworld 800\nagain 700\n";

    public SuggestionCoordinatorSafetyTests() : base(Advisor()) { }

    [Theory]
    [InlineData("field")]
    [InlineData("epoch")]
    [InlineData("suffix")]
    [InlineData("selection")]
    [InlineData("secure")]
    [InlineData("unreliable")]
    public async Task GenerationDropsAnyNonExactFieldSnapshot(string change)
    {
        await RunOnUiAsync(() => { });
        var original = Fields.Text("hello world") with { CaretOffset = 6 };
        Focus.Replace(original);

        var generationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGeneration = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        Engine.GenerateOverride = async (_, ct) =>
        {
            generationStarted.TrySetResult(true);
            return await releaseGeneration.Task.WaitAsync(ct).ConfigureAwait(false);
        };

        Task prediction = InvokePrivateTask(
            "PredictAsync",
            "req_safety",
            Coordinator.Settings,
            original,
            0,
            null,
            0L,
            CurrentEditEpoch(),
            CancellationToken.None);

        try
        {
            await generationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Focus.Replace(change switch
            {
                "field" => original with { FieldId = "another-field" },
                "epoch" => original with { FocusChangeSequence = original.FocusChangeSequence + 1 },
                "suffix" => original with { Text = "hello earth", CaretOffset = 6 },
                "selection" => original with { HasSelection = true },
                "secure" => original with { IsSecure = true },
                "unreliable" => original with { HasReliableSelection = false },
                _ => throw new ArgumentOutOfRangeException(nameof(change)),
            });
            releaseGeneration.TrySetResult(" there");

            await prediction.WaitAsync(TimeSpan.FromSeconds(5));
            await RunOnUiAsync(() => { });
        }
        finally
        {
            releaseGeneration.TrySetResult(" there");
            if (!prediction.IsCompleted) await prediction.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.Equal("", Overlay.Text);
    }

    [Fact]
    public async Task PresentRejectsStaleCachedSnapshotWithoutRefreshingOnUiThread()
    {
        await RunOnUiAsync(() => { });
        var original = Focus.Current!;
        Focus.Replace(original with
        {
            FieldId = "different-field",
            FocusChangeSequence = original.FocusChangeSequence + 1,
        });
        long editEpoch = CurrentEditEpoch();
        var present = CoordinatorMethod("Present");

        await RunOnUiAsync(() => present.Invoke(Coordinator,
        [
            "req_cached",
            " continuation",
            original,
            0,
            0L,
            editEpoch,
            CancellationToken.None,
            null,
            null,
        ]));

        Assert.Equal("", Overlay.Text);
        Assert.Equal(0, Focus.RefreshesOnPump);
    }

    [Fact]
    public async Task AcceptEpochCapturedOnHookCannotBeRenewedAfterEscapeBeforeUiProcessing()
    {
        Type("hello");
        await Shown(" world again");

        var uiEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUi = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockUiUntil(releaseUi.Task, uiEntered);
        try
        {
            await uiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Hook.Press(Keys.Tab()));
            Hook.Press(Keys.Special(KeyKind.Escape, Keys.VkEscape));
        }
        finally
        {
            releaseUi.TrySetResult(true);
        }

        await Shown("");
        await RunOnUiAsync(() => { });
        await GetPrivateField<Task>("_insertChain").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(Focus.Insertions);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("navigation")]
    [InlineData("dismissal")]
    [InlineData("focus")]
    [InlineData("settings")]
    [InlineData("model")]
    public async Task PendingAcceptReceivesAndHonorsInvalidationToken(string invalidation)
    {
        Type("hello");
        await Shown(" world again");

        var insertStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var insertCanceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken dispatchedToken = default;
        Focus.InsertOverride = async (_, _, ct) =>
        {
            dispatchedToken = ct;
            insertStarted.TrySetResult(true);
            try
            {
                await neverRelease.Task.WaitAsync(ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                insertCanceled.TrySetResult(true);
                return false;
            }
        };

        Assert.True(Hook.Press(Keys.Tab()));
        await insertStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(Focus.InsertTargets);

        long epochBeforeInvalidation = CurrentEditEpoch();
        switch (invalidation)
        {
            case "ordinary":
                Hook.Press(Keys.Char('x'));
                break;
            case "navigation":
                Hook.Press(Keys.Special(KeyKind.Arrow, 0x25));
                break;
            case "dismissal":
                Hook.Press(Keys.Special(KeyKind.Escape, Keys.VkEscape));
                break;
            case "focus":
                Focus.Focus(Fields.Text("other", process: "other", pid: 200) with { FieldId = "other" });
                break;
            case "settings":
                Coordinator.Settings = Coordinator.Settings with { Enabled = false };
                break;
            case "model":
                Coordinator.InvalidateSession();
                break;
        }

        Assert.True(CurrentEditEpoch() > epochBeforeInvalidation, $"{invalidation} should revoke the pending edit epoch");
        Assert.True(dispatchedToken.IsCancellationRequested, $"{invalidation} should cancel the token passed to the inserter");

        await insertCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await GetPrivateField<Task>("_insertChain").WaitAsync(TimeSpan.FromSeconds(5));
        await RunOnUiAsync(() => { });
        Assert.Empty(Focus.Insertions);
    }

    [Fact]
    public async Task ReanchorCannotMoveAnExistingGhostToAnotherFieldOrEpoch()
    {
        Type("hello");
        await Shown(" world again");
        var anchorBefore = Overlay.Anchor;
        var expectedField = GetPrivateField<FocusedField>("_sessionField");
        var session = GetPrivateField<SuggestionSession>("_session");

        var uiEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseUi = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockUiUntil(releaseUi.Task, uiEntered);
        try
        {
            await uiEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task reanchor = InvokePrivateTask("ReanchorAsync", session.RequestId, 0, expectedField);
            await reanchor.WaitAsync(TimeSpan.FromSeconds(5));
            Focus.Replace(expectedField with
            {
                FieldId = "another-field",
                FocusChangeSequence = expectedField.FocusChangeSequence + 1,
            });
        }
        finally
        {
            releaseUi.TrySetResult(true);
        }
        await RunOnUiAsync(() => { });

        Assert.Equal(anchorBefore, Overlay.Anchor);
        Assert.Equal(" world again", Overlay.Text);
    }

    [Fact]
    public async Task AcceptedEditRejectsMovedCaretInsideARepetitiveDocument()
    {
        await RunOnUiAsync(() => { });
        const int offeredRunLength = 1024;
        const int movedRunLength = 1280;
        var original = Fields.Text(new string('a', offeredRunLength) + " tail") with
        {
            CaretOffset = offeredRunLength + 1,
        };
        Focus.Replace(original);
        var present = CoordinatorMethod("Present");
        await RunOnUiAsync(() => present.Invoke(Coordinator,
        [
            "req_repeated",
            " next",
            original,
            0,
            0L,
            CurrentEditEpoch(),
            CancellationToken.None,
            null,
            null,
        ]));
        await Shown(" next");

        // The last 256 code units before either caret are identical, and the visible suffix is
        // unchanged. A fuzzy tail match would accept at the wrong offset in this repeated run.
        Focus.Replace(original with
        {
            Text = new string('a', movedRunLength) + " tail",
            CaretOffset = movedRunLength + 1,
        });

        Assert.True(Hook.Press(Keys.Tab()));
        await RunOnUiAsync(() => { });
        await GetPrivateField<Task>("_insertChain").WaitAsync(TimeSpan.FromSeconds(5));
        await RunOnUiAsync(() => { });

        Assert.Empty(Focus.InsertTargets);
        Assert.Empty(Focus.Insertions);
    }

    [Fact]
    public async Task CorrectionAcceptPassesItsOriginalSnapshotToReplacement()
    {
        Coordinator.Settings = SuggestionSettings.Default with
        {
            Typos = new TypoGateSettings(
                SuppressCompletionsOnTypo: true,
                OfferTypoCorrections: true,
                AutomaticallyFixTypos: false),
        };
        Type("hello teh ");
        await Shown("the");
        var expected = Focus.Current!;
        var replacementCalled = new TaskCompletionSource<FocusedField>(TaskCreationOptions.RunContinuationsAsynchronously);
        Focus.ReplaceOverride = (target, _, _, _) =>
        {
            replacementCalled.TrySetResult(target);
            return Task.FromResult(false);
        };

        Assert.True(Hook.Press(Keys.Tab()));
        var passedTarget = await replacementCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(expected.FieldId, passedTarget.FieldId);
        Assert.Equal(expected.FocusChangeSequence, passedTarget.FocusChangeSequence);
        Assert.Same(expected, passedTarget);
        await GetPrivateField<Task>("_insertChain").WaitAsync(TimeSpan.FromSeconds(5));
        await RunOnUiAsync(() => { });
    }

    private static TypoCorrectionAdvisor Advisor()
    {
        var corrector = TestCorrector.Loaded((SpellingDictionaryLanguage.English, Dictionary));
        return new TypoCorrectionAdvisor(corrector, new SpellingLanguageResolver());
    }

    private long CurrentEditEpoch()
    {
        var lifetime = GetPrivateField<object>("_pendingEdits");
        var capture = lifetime.GetType().GetMethod("CaptureEpoch", BindingFlags.Instance | BindingFlags.Public)!;
        return (long)capture.Invoke(lifetime, null)!;
    }

    private T GetPrivateField<T>(string name)
    {
        var field = typeof(SuggestionCoordinator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T)field.GetValue(Coordinator)!;
    }

    private static MethodInfo CoordinatorMethod(string name) =>
        typeof(SuggestionCoordinator).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    private Task InvokePrivateTask(string name, params object?[] arguments)
    {
        var method = CoordinatorMethod(name);
        return (Task)method.Invoke(Coordinator, arguments)!;
    }

}
