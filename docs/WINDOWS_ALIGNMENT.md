# Frozen Mac alignment campaign

The maintainer approved this campaign on 2026-10-29. The stable product and design reference is
Mac **`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`**. Subsequent Mac commits are outside this campaign
until explicitly adopted. Historical module/fixture references remain unchanged until individually
upgraded; the campaign pin does not claim existing modules already match it.

## Scope and completion

Alignment includes functionality, user flow, interaction, UI structure and visual design. Every
user-facing module traces its navigation, windows/cards, information hierarchy, control placement,
typography hierarchy, spacing, color, icons, density and state feedback to committed Mac evidence.
Loading, empty, failure, retry, disabled, selection and cancellation states are part of the scope.
Keyboard/focus/activation, dismissal, resizing and accessibility belong to the same module.

Use existing Mac views, styles, assets and tests as authority. Translate AppKit/SwiftUI to WPF/Win32
while retaining the product contract. Native Windows chrome, available fonts, DPI and OS input
conventions may require documented differences. No replacement product or design system is implied.
Runtime names, user-data destinations and identifiers retain the existing Cotabby compatibility
boundary until an explicit migration is designed.

Each handoff includes source SHA/paths, Windows mapping, functional checks, representative rendered
Windows views/screenshots, available Mac visual references, known differences and pending native
acceptance. Missing Mac screenshots are an evidence gap; source-based review alone is not a claimed
pixel comparison. Test counts and successful XAML builds do not replace visible or desktop checks.

## Team and file ownership

The primary agent orchestrates three implementation delegates, all using `gpt-6.1-terra` with
`xhigh` reasoning. Each delegate owns a complete module through source audit, implementation,
tests, module documentation and review repairs. Work is delivered through bounded checkpoints,
not a single unreviewable whole-product change.

| Owner | First module | Boundaries |
| --- | --- | --- |
| A | Frozen vocabulary resources and durable storage | Canonical resource/semantic/classification values, graph admission, codecs/database/archive, migration, Store and export/backup data integrity. Owns resource contracts consumed by C. |
| B | Writing autocomplete and model management | Frozen task/model compatibility and assignment, runtime isolation, completion behavior, relevant settings surfaces and Windows input/focus adapters. Coordinates shared settings/composition through primary. |
| C | Frozen notebook user workflows and design | Latest notebook list/detail, creation/import and meaning/classification UI, associated pure form/browser policies and WPF tests. Uses A's resource/Store contracts. |
| Primary | Shared integration and acceptance | AGENTS/architecture/baseline/parity/plan, shared settings and AppHost composition, CI/project integration, interface decisions, review coordination, Git/PR lifecycle and physical desktop acceptance. |

Each implementation owner uses an isolated managed worktree and `codex/` branch. Before editing,
declare the intended file set. A file has one active writer; changes to another owner's shared
boundary are proposed to that owner/primary first. Root-wide documents, CI, project files, AppHost
and the shared settings document are integrated by primary. Module-specific documents/tests stay
with their module. Windows builds use each worktree's output so the maintainer's preview stays
separate from in-progress binaries.

A publishes exact resource/Store contracts before C connects acknowledged writes. C can audit and
implement independent UI/form policies meanwhile, without a second Store, production fake service
or temporary persistence format. B can advance independently within the same composition rules.
No migration experiment uses the maintainer's live notebook; isolated databases/media establish
data retention, reopen and rollback before application activation.

## Delivery and review

1. Audit only the frozen source/test/design evidence and document the module delta and first
   bounded checkpoint. Resolve shared contracts before cross-owner implementation.
2. Implement source-derived behavior and its Windows effects, keeping privacy, cancellation,
   stable identity and single-owner data/runtime contracts.
3. Run appropriate narrow checks and a Release build. Broaden checks when changes or unresolved
   concerns require them. Keep historical fixture expectations and pins intact.
4. Obtain independent review from another delegate/primary; an author is not the sole reviewer.
   All delegates use Terra xhigh under the current maintainer instruction. Reviewers report
   findings; the implementation owner repairs them.
5. Integrate acknowledged checkpoints, run final-head required CI, then merge scoped PRs under the
   existing maintainer authorization. Missing remote authentication does not prevent isolated local
   work, but no push/PR/merge is claimed without successful remote evidence.
6. Present a runnable Windows checkpoint and the precise remaining UI/native acceptance. Ordinary
   rounds create no installer, release, downloadable package, uploaded build artifact or model download.

After the first modules, queue expression assistance, selected-text/reading/OCR, complete Anki
preparation/layout/batch/conflicts, CLI, onboarding/localization and speech integration according
to their actual frozen dependencies. Reassign a free owner to a complete next module. Audio
expansion remains behind the visible writing/notebook/review workflows.

## Bootstrap status

PR #108 supplies the older v7 resource value/projection boundary and Windows handoff; it is a
prerequisite, not a completed v8 archive. It merged as `c911336` after independent review and all
three final-head CI jobs passed for `b5297d5`.
The primary repaired the local Windows file-lock fixture to run a static inline child command,
passing the path as data without changing execution policy. The documented diagnostic uses a
process-only RemoteSigned invocation. The focused lock test passes 1/1, the full noninteractive
Inference suite passes 578/578, and the documented prerequisite check reports Ready on this host.
Independent review and remote final-head CI remain separate gates.

Combined validation exposed a second test-only Windows race: `File.ReadAllLines` can conflict with
the helper's concurrent append. The marker reader now shares the append-only file and publishes
only complete newline-terminated snapshot records. The existing native process suite passes 5/5,
the complete noninteractive Inference suite passes 578/578, and portable App passes 1222/1222.
Independent read-only review checked the writer and reader without a remaining blocker. This does
not change the speech protocol or activate an audio provider.

Executed from the integration worktree with Git/.NET added only to the process PATH and .NET
telemetry disabled:

```powershell
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --filter 'FullyQualifiedName~VocabularyWindowsFileTests.ExclusiveLockInAnotherProcessBlocksOurSharedAccessAndReleasesCleanly'
powershell -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\check-windows-dev.ps1 -Json
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --no-build --no-restore --filter 'FullyQualifiedName!~ClipboardTextReaderTests'
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release --filter FullyQualifiedName~WindowsSpeechProcessTests
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
```
