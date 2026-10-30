# Mac alignment campaign

The maintainer approved this campaign on 2026-10-29 and subsequently authorized the latest Mac
functionality. The current committed product/design reference is
Mac **`3b53402f2cbc196b0398d133177fd80006d165b2`**. Record and inspect later targets before adoption.
Historical module/fixture references remain unchanged until individually
upgraded; the campaign pin does not claim existing modules already match it.

## Scope and completion

Alignment includes functionality, user flow, interaction, information architecture (IA), content
hierarchy and state expression. On 2026-10-29 the maintainer clarified that Windows need not visually
duplicate Mac: the goal is an attractive, coherent, usable Windows product. Loading, empty, failure,
retry, disabled, selection and cancellation states remain part of the scope, as do keyboard,
focus/activation, dismissal, resizing and accessibility semantics.

Use committed Mac views and tests as the behavior, IA and state reference. Adapt AppKit/SwiftUI to
WPF/Win32 while retaining navigation, information organization, operation discoverability, hierarchy
and observable outcomes. Controls, typography, icons, spacing, colors and window chrome may use
Windows/WPF's own design strengths; pixel equality and identical component geometry are not required.
Review visual choices for beauty, clarity and consistency, and record Windows input, DPI and font
choices. This visual freedom does not authorize a replacement product flow or business architecture.
Runtime names, settings and the data root retain their existing Cotabby identities. Under the
maintainer's subsequent 2026-10-29 clarification, old-data compatibility and migration are not
prerequisites: activate the canonical notebook using a fresh versioned filename in that root,
preserving older files without automatic conversion or overwrite.

Each handoff includes source SHA/paths, Windows mapping, functional checks, representative rendered
Windows views/screenshots, available Mac visual references, known differences and pending native
acceptance. Use available Mac images to assess hierarchy and state expression, not pixel identity.
Missing Mac screenshots limit direct comparison and are recorded without blocking source-based IA
review. Test counts and successful XAML builds do not replace visible or desktop checks.

## Team and file ownership

On 2026-10-30 the maintainer ended delegation and required the primary agent to perform all further
code work alone. The primary has interrupted every active delegate and preserved their committed
and uncommitted work. Do not spawn, resume or assign subagents, including review agents, unless the
maintainer explicitly changes this instruction. The team table below records the earlier campaign,
not the current execution model. Further work uses direct source review and executable validation
by the primary, retaining the existing required CI and final physical acceptance boundaries.

The maintainer reiterated on 2026-10-30 that further delivery must use complete functional blocks,
including behavior, UI, production entries/settings and regression validation. Do not split the
remaining gaps into small standalone library deliveries. The installed .NET team skills are
`csharp-expert`, `msbuild`, `dotnet-pinvoke` and `test-anti-patterns`, pinned to upstream
`dotnet/skills` commit `95e7d0cb7994d47b5789baa68e13f7a2f348b956`. Load only the skill appropriate
to the current work; they do not authorize delegates, a UI-framework migration, architecture
replacement or changes to the committed Mac behavior contract.

The primary agent orchestrates three implementation delegates, all using `gpt-6.1-terra` with
`high` reasoning for subsequent dispatches, as clarified by the maintainer on 2026-10-29. Existing
running assignments may finish their checkpoint before the next dispatch adopts this setting.
Each delegate owns a complete module through source audit, implementation,
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
Current-format experiments use isolated databases/media to establish identities, reopen, rollback
and media integrity. Legacy Windows conversion and cross-OS binary admission are out of scope.
Ready canonical components should advance to production composition against a fresh versioned
file after review and required checks; data compatibility must not keep them artificially inactive.
AppHost must select one Store for notebook and writing capture, with no parallel legacy writer.

## Delivery and review

The maintainer's subsequent delivery adjustment groups work by complete user flows, including
their settings and production entries. Internal commits keep targeted checks; independent review
and required CI gate the complete candidate. Physical acceptance is consolidated at the end and
handed to the maintainer, rather than blocking development on interim desktop automation.

1. Audit only the frozen source/test/design evidence and document the module delta and first
   bounded checkpoint. Resolve shared contracts before cross-owner implementation.
2. Implement source-derived behavior and its Windows effects, keeping privacy, cancellation,
   stable identity and single-owner data/runtime contracts.
3. Run appropriate narrow checks and a Release build. Broaden checks when changes or unresolved
   concerns require them. Keep historical fixture expectations and pins intact.
4. Preserve independent review already recorded for earlier modules. Under the maintainer's later
   solo-work instruction, the primary reviews further edits against committed Mac contracts and
   meaningful tests without assigning another agent. Do not represent that as independent review.
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
