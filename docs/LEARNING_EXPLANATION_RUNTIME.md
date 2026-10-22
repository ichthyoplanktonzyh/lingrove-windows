# Local explanation app ownership

This slice adapts the pinned Mac implementation at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. AppHost now instantiates one local explanation
controller/service with its **own** lazy runtime. Settings UI can select an installed model, a
role-appropriate template, response language and per-model context/output limits. An interactive
explanation session/card and settings test action remain unwired; selecting a model does not run it.
The [portable session library](LEARNING_EXPLANATION_SESSION.md) now owns target/refresh/cancellation
state, while its WPF adapter is still a separate step.

## Source mapping

| Pinned Mac source / tests | Windows adaptation | Ownership and data flow |
| --- | --- | --- |
| `Models/LanguageLearning/LearningModelSettings.swift`: explanation role, snapshot, parameters; `LearningModelSettingsTests.swift` | `App/Settings/AppSettings.Explanation.cs` and existing settings parser | Independent local model/template, per-model context/generation preferences and response language become a detached effective snapshot. Existing translation/autocomplete keys are neither migrated nor inherited. |
| `LanguageLearningSettings.resolvedExplanationLanguage` | `ResolvedLearningExplanationLanguage` | Explicit language wins. Unset follows the app language; the current Windows UI is English, so its fallback is English. This does not claim full localization parity. |
| `LearningModelSnapshot.usesSameRuntime` | `Core/LanguageLearning/LearningModelSnapshot.UsesSameRuntime` | Style, sampling and output budget affect requests; model/template/context identify the allocation. |
| `Services/LanguageLearning/LearningModelCollection.swift`; `LearningModelController.profileChanged/applyResidency/scheduleIdleRelease/shutdownSync`; `LearningModelResidencyTests.swift` | `App/Learning/LearningExplanationController.cs`, `Hosting/AppHost.cs` | Global eligibility retires native work; explanation stays on demand, releases after 60 idle seconds, and drains before its runtime is disposed. Translation enablement does not gate explanation. |
| `UI/Settings/Panes/Engine/LearningModelConfigurationView.swift` | Role-aware `LearningLocalModels.cs`, `LearningPane.cs` | Shared file-manager/catalog controls write only the selected role. Explanation excludes specialist template choices and disables known non-instruction model choices. Existing stored choices remain intact and the actual metadata gate still decides generation. |

## Preserved behavior and Windows boundaries

The explanation preference keys are new Windows keys. Missing old keys leave this role local and
unconfigured, with Automatic framing, 8,192 context tokens, recommended sampling and an implicit
1,024 output-token default. Per-model context is bounded to 1,024–32,768 and explicit output to
256–4,096, matching the existing Mac-backed policies. Explanation ignores translation style and
translation-specialist context recommendations. Dictionaries recover Windows filename comparison
after JSON decoding; this is an OS adaptation, not a shared storage format with macOS.

AppHost creates a third independent `LearningLocalRuntime`; no native sequence, resident weights,
prompt/cache or cancellation source is borrowed from translation or autocomplete. Construction and
settings changes never prewarm explanation. A service request creates a one-use local facade from
the frozen role snapshot. The installed filename cannot escape the shared model directory. The
real provider retains the operation-scoped metadata, general-instruction, exact-token, EOG and raw
response validation gates documented in [the explanation rules](LEARNING_EXPLANATION_RULES.md).

Global disable, temporary pause and opted-in battery-saver pause retire in-flight work and release
possible native allocations. Explicit generation rechecks live eligibility before and after the
provider, including a power change whose event has not yet arrived. Translation disable/provider
changes do not affect explanation. Expired pauses are checked live at request admission; a separate
pause-expiration publisher is not yet ported, so internal residency can remain Released until a
request or another settings/power event reconciles it. No warmup is needed on expiry.

Effective profile changes cancel old requests. Allocation changes also request native release;
request-only sampler/output changes retain weights and restart the idle deadline as Mac does.
Every facade drains before its active count drops. The last facade schedules the 60-second timer,
including failed preparation that may already have allocated memory. Disposed timer callbacks carry
a revision check so a queued old deadline cannot unload a later request. Native unload itself records
retirement synchronously and drains on its worker, with its existing latest-owner guards.

Shutdown closes service admission synchronously, including saved/cache paths, then drains service
requests, facades and outstanding release tasks before disposing the explanation runtime. Windows
thread guards replace MainActor sequencing; no UIA/editor/window objects enter this owner. Saved
results can still be viewed without generating, as in the Mac service. Telemetry, endpoint settings/
consent/credentials, automatic power model switching, GPU controls, notebook persistence, settings
sample test, sampling-override editor, explanation session/card and the remaining capture origins
are separate work. Sampler preferences are persisted and honored, but currently edited only through
settings data; the UI keeps recommended sampling by default. The empty model picker item clears
only that role's selection, retaining downloaded files and saved per-model preferences.

## Validation boundary

`tests/Cotabby.App.Tests` compiles the actual non-WPF settings/controller source with Core and
Inference contracts on both macOS and Windows. Deterministic effect doubles and timers check
independent defaults/roundtrip, parameter normalization, no startup loading, idle release/reload,
old timer rejection, request/allocation edits, global pause/power cancellation, stale output,
filename confinement and shutdown order. One test runs the actual local facade over a fake native
operation to check the real configuration/metadata handoff, without a GGUF or native model library.
Core tests separately cover allocation identity and service retirement preserving complete cache.

```sh
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
dotnet build Cotabby.sln --configuration Release -p:EnableWindowsTargeting=true
```

Both CI jobs execute the app-boundary tests. The Windows job still builds the complete WPF app and
runs the native adapter suite; macOS still runs all six pinned Swift verifiers. These tests establish
portable policy/lifetime behavior and successful compilation, not WPF interaction, Windows UIA/IME,
model execution, GPU routing or explanation quality. CI downloads no model weights and creates no
installers, downloadable app packages, uploads or releases.
