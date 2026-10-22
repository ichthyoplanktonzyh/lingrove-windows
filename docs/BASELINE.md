# Windows Port Baseline

This note records the source baseline used for the first Lingrove Windows foundation milestone.
It separates what the repository contains from what has been demonstrated on a Windows desktop.

## Pinned inputs

| Input | Revision | Use |
| --- | --- | --- |
| Windows fork | `b1e4031c70aca1be21aae2c57b138fde5fc22bb1` | Baseline for code inventory and existing tests |
| macOS reference | `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f` | Committed source and test evidence for parity |
| Windows work branch | `codex/windows-foundation` | First documentation, CI, and shared-fixture milestone |

Mac references in these docs were read from committed paths with `git show <revision>:<path>`. The
macOS working tree may contain unrelated edits and is not evidence for the matrix.

## What exists in the Windows fork

The repository is a C#/.NET WPF application split across four production projects:

- `Cotabby.Core` contains portable autocomplete policies, context filtering, emoji, macros,
  autocorrection rules, settings values, and the suggestion coordinator/session.
- `Cotabby.Inference` contains LLamaSharp CPU/Vulkan integration and an OpenAI-compatible endpoint
  client and engine.
- `Cotabby.Win32` contains UI Automation focus tracking, the low-level keyboard hook, Win32 text
  insertion, clipboard text access, DPAPI credential storage, Windows spell checking, and power
  observation.
- `Cotabby.App` contains WPF windows and controls, tray UI, settings presentation, and `AppHost`,
  which assembles the long-lived services.

`tests/Cotabby.Core.Tests/` covers many deterministic policy areas, including request construction,
normalization, acceptance, context sanitation, focus classifiers, endpoint configuration, emoji,
macros, and spelling. Inference tests and smoke tools are present in the solution. These files show
intent and test coverage; they do not establish that the app has been exercised in real Windows
editors or that every test has passed in the current environment.

The Windows tree has no language-learning coordinator, reading/selection/OCR capture flow, dictionary
provider, vocabulary database/notebook, Anki support, speech runtime, typing-history store, vocabulary
CLI, or interface localization subsystem. Its autocomplete endpoint and local model paths are not
learning runtimes. See [PARITY.md](PARITY.md) for the full feature inventory.

## Known gaps and verification limits

- The WPF ghost overlay and UIA focus tracker are platform-specific implementations. They have not
  been validated in a representative matrix of native, browser, Electron, mail, and Office fields.
- UIA snapshots may be delayed, incomplete, or invalidated by focus changes. Caret, selection, secure
  field, stale-result, and insertion checks need native host evidence.
- The keyboard hook has not been shown to respect IME preedit in every Windows input method. Treat
  IME exclusion as a missing safety contract until an explicit signal and test cover it.
- `SendInputTextInserter` uses clipboard text for longer pastes and snapshots only text. The source
  documents possible loss of non-text formats; rich clipboard restoration and concurrent user copy
  behavior remain open work.
- Windows source includes Vulkan support, but x64 inference on a supported Windows GPU has not been
  demonstrated by these docs. A passing CPU/portable suite does not verify Vulkan.
- The existing source baseline targeted .NET 9. The foundation milestone moves the repository to
  .NET 10; this is a toolchain update, not evidence of product or language-learning parity.
- At the time of this baseline, the maintainer has an Apple M1 Max Mac with 32 GB RAM and no Windows
  desktop. A hosted runner can validate build and tests; it cannot verify the user's actual field,
  IME, overlay, clipboard, or GPU experience. ARM Windows virtualization may catch ARM issues but is
  not x64 or x64 Vulkan validation.

## CI jobs and validation snapshot

The current `.github/workflows/ci.yml` is the .NET 10 target workflow:

- `windows` runs on Windows x64, restores and builds the full solution, then executes Core and
  Inference adapter/transport tests. The interactive clipboard adapter test is excluded.
- `portable-core` runs on macOS 26, executes the portable Core tests, checks out only the `Support`
  subtree from the pinned macOS commit, and runs `scripts/verify-macos-parity.py` against the same
  JSON corpus. The script extracts committed pure policies; it does not build the macOS app or
  involve UI Automation, inference, or model downloads.

The separate `.github/workflows/upstream-baseline.yml` is a historical checkpoint. It runs only
when that workflow or this baseline document changes, or on manual dispatch. It checks out the
original fork commit `b1e4031c70aca1be21aae2c57b138fde5fc22bb1`, uses .NET 9 on Windows x64, and runs
the original build, Core tests, and non-interactive Inference tests. It does not validate the current
.NET 10 changes.

Recorded results for 2026-10-22:

- The upstream baseline run [39725452207](https://github.com/ichthyoplanktonzyh/lingrove-windows/actions/runs/39725452207)
  succeeded: build passed, with 1,555 Core tests and 142 Inference adapter/transport tests passing;
  the clipboard UI test was excluded.
- The local macOS Release run of `Cotabby.Core.Tests` passed 1,572 tests. Four date-test expectations
  were made culture-aware so the same portable cases pass on Windows and macOS; no product behavior
  changed.
- The Swift verifier passed all 17 JSON parity cases against the committed macOS pure policies.
- The new .NET 10 `ci.yml` workflow has not run yet. No native Windows desktop, UI Automation,
  insertion, IME, or GPU validation has been performed. No model-backed smoke project was run.

These results establish automated source-level evidence for the named suites only. They do not mark
the full app or native host behavior verified in [PARITY.md](PARITY.md).

Reproduce the two local macOS checks from the repository root with the repo-local SDK:

```sh
export DOTNET_ROOT="$PWD/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-macos-parity.py --mac-repo /Users/shadow/lingrove
```

The shared corpus covers only pure acceptance chunking, ghost spacing, and normalization behavior;
it does not cover session freshness, UI Automation, host insertion, or end-to-end autocomplete.

## CI and distribution boundary

The initial CI contract is restore, build, and test. It does not fetch model weights, create an
installer, package a downloadable application, upload build artifacts, publish, or create a release
for each commit. There is no product-download promise in this milestone. Local model downloads, if
the inherited app exposes them, are user-run application behavior and must never become a CI step.

## Compatibility and provenance

The source retains Cotabby namespaces and binary/settings identifiers as a migration compatibility
choice. This does not certify an on-disk data format as compatible with the macOS app. The repository
continues under AGPL-3.0; preserve the upstream Cotabby attribution and the third-party notices in
[`LICENSE`](../LICENSE) and [`THIRD_PARTY_LICENSES.md`](../THIRD_PARTY_LICENSES.md). Do not remove or
relabel dependency and model licenses during the branding transition.
