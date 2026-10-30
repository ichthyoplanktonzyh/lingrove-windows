# Website and online dictionary sources

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
committed files under `Lingrove/`:

- `Services/LanguageLearning/LearningDictionaryRouter.swift` and `OnlineDictionaryClient.swift`;
- `Support/LanguageLearning/DictionaryWebsitePolicy.swift`;
- `Models/LanguageLearning/DictionaryLanguagePair.swift` and `LearningDictionarySettings.swift`;
- `UI/Settings/Panes/DictionarySettingsSection.swift` and `DictionaryWebsiteEditor.swift`;
- `UI/InlineFeatures/LearningDictionaryContentView.swift`.

The shared app dictionary feature owns one router and online HTTP client; the writing and notebook
sessions borrow it. Website settings retain IDs, language metadata, order and enablement. The eight
source recommendations retain their Mac IDs and start disabled when added. Custom templates must
contain one `{query}`, use HTTPS, and cannot put the placeholder or credentials in the authority.
The selected text is percent-encoded as an opaque component. Selecting a website creates a visible
full destination and an explicit browser action; it never loads the site inside the private MDict
WebView. Removing a source changes settings only.

The online English provider targets `https://api.dictionaryapi.dev`, sends only the selected lookup
term, and does not fetch audio. Requests disable cookies, redirects and cache; response reading is
cancellable and bounded to one million bytes, with ten-second header and fifteen-second total
deadlines. The decoder retains source/attribution, license, phonetics, definitions and examples.
It rejects null required fields throughout the complete decoded graph before taking display
prefixes, and uses the existing canonical scalar comparer for Swift string ordering. Errors are
presented without request details. Apple system dictionaries remain unavailable.

Windows privacy adaptation: enabling the online source does not add it to automatic fallback.
Selecting it shows its destination and the data sent; a separate **Look up online** action admits
that request. A changed source, target, or settings revokes the displayed offer. This deliberately
differs from Mac 3b's enabled-source automatic fallback. Local bundled lookup remains the default.
Failed online requests retain their destination disclosure and require an explicit current Retry
action; stale retry buttons cannot admit another request.

The effect boundary is `Inference/LanguageLearning/OnlineDictionaryClient.cs`; portable URL/decode
rules live in Core. `App/Dictionary/DictionaryFeature.cs` owns the one client, settings and router.
Shutdown closes HTTP admission, cancels and joins accepted work before disposing its pool, even if
a retired settings window or local book service fails. A displayed offer alone allocates no request
lifetime. Floating cards and both notebook adapters borrow their existing session and validate the
displayed request and owner before a lookup or external-link action.

The WPF source settings use the shared light/dark palette, Segoe UI, wrapping source cards and
keyboard-accessible reorder controls at a 480-DIP minimum width. The isolated website draft keeps
stable identity, ordering and enablement on Save, validates before enabling the default button,
and supports Cancel/Escape without publication. Website navigation stays in the system browser;
the local MDict WebView and its profile ownership remain separate.

Tests use synthetic data and an injected HTTP handler; no live endpoint, user dictionary or browser
is contacted. Swift/native parity and physical browser, accessibility, DPI, and network acceptance
are separate. The MDict isolated WebView, two-slot owner and profile cleanup are unchanged.

Primary-only implementation/source review follows the maintainer's 2026-10-30 instruction to stop
delegation. This is not an independent agent review. Targeted checks include 95 portable dictionary
cases, five fake-HTTP transport cases and 69 compiled WPF dictionary/settings cases. Rendered the
real online-offer card, plus light/dark website forms and source lists at 96-DPI output; inspected
the form and 480-DIP source list for hierarchy and wrapping. No matching Mac screenshots were
available, so IA comparison is source-based. Live HTTPS, physical browser launching, high-DPI and
screen-reader/keyboard desktop acceptance remain for the maintainer's final unified acceptance.

Full local validation of code commit `2b5aa2ab7c4c23f3d1dd32fc9bb01a6c84c4eb02` on the physical
Windows x64 development host (merging main at `a13a980` changed no file content):

```powershell
dotnet build Cotabby.sln --configuration Release --no-restore --nologo -v minimal
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release --no-build --no-restore
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release --no-build --no-restore
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --no-build --no-restore
./scripts/test-wpf-dev.ps1 -NoBuild -NoRestore
```

`LINGROVE_TEST_SYSTEM_SPEECH=0` and `COTABBY_CLIPBOARD_TEST=0`. Build: zero warnings/errors.
All passed without skips: Core 5,079; App 1,278; Inference 750; WPF 1,047 (3m40s). The WPF helper
held the named desktop lease. No model, live API, browser launch, user notebook/editor, clipboard
or audio-device acceptance was executed. Remote required checks are evaluated on the PR's final head.

The first remote Windows run passed compilation and the other three local test assemblies but
failed the existing `CliLargeGraphTests.LargeBatchCanBeCancelledBeforePublication`: its 50ms
cancellation timer could run after the plan finished. The primary reviewed all three tests in that
file using the installed .NET test anti-pattern guidance: one High reliability finding, no Critical,
Medium or Low findings. The two successful-plan tests retain exact graph-count/evidence assertions;
their timing/allocation output is diagnostic, not a pass/fail threshold. The cancellation case now
uses the existing prepared-media enumeration boundary after a first operation to cancel before the
large capture. It verifies the caller's exact cancellation token and the unchanged baseline. No
production cancellation rules, historical fixtures, timeout budgets or CI gates were changed.
After the repair, the focused CLI group passed 17/17, full Core passed 5,079/5,079, and the exact
cancellation regression passed five further independent runs. The other production assemblies
are unchanged from the earlier complete local validation. Required remote checks must pass again
for the repaired head before merging.
