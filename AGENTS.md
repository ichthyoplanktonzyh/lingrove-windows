# Lingrove Windows Contributor Instructions

## Product scope

Lingrove has two equally core product capabilities: local-first autocomplete while writing and
language learning while writing. Treat both as first-class product work. The current Windows code is
an inherited Cotabby autocomplete port; language learning is a porting goal, not an implemented
Windows feature. Do not describe planned learning behavior as shipped or verified.

This repository is the Windows port of the macOS Lingrove project and retains its AGPL-3.0 license
and upstream attribution. During the migration, keep existing `Cotabby` namespaces, assemblies,
binary names, settings locations, and storage identifiers stable unless a specific migration is
designed and documented. Lingrove branding in documentation does not mean the Windows runtime has
been rebranded or that its data is compatible with the macOS app.

## Migration principle: adapt the stable macOS product

**Windows 版本是稳定 macOS 成果的适配与移植，不是重新发明产品、交互或业务架构。**

- Treat the pinned macOS implementation and its tests as the behavior reference. Read the relevant
  Mac subsystem before implementing its Windows counterpart; preserve its responsibilities, data
  contracts, defaults, user flow, prompting, budgets, cancellation, and privacy semantics.
- Port deterministic rules and regression cases directly into C# where possible. Do not invent a
  simpler replacement rule, change expected results to fit the Windows fork, or redesign a workflow
  merely because the implementation language or UI framework differs.
- Adapt the platform-dependent implementation: Accessibility/AppKit/SwiftUI become Windows UIA,
  Win32 and WPF, and unavailable Apple-specific providers require a Windows-supported counterpart.
  Keep the Mac module's responsibility and observable behavior wherever the platform permits.
- Record each migrated module's Mac source/test paths and reference revision, its Windows mapping,
  necessary platform differences, and remaining verification in the parity or stage documentation.
  A platform limitation is an explicit gap, not permission to silently change the product contract.
- Use architecture/design skills to understand the existing Mac design and place necessary Windows
  adapters. Their use does not authorize a new product design or a replacement business architecture.
  Product redesign belongs to separately scoped work, outside ordinary porting.
- A round may implement a documented subset of the Mac feature. Keep the remaining behavior visible
  as incomplete; do not substitute fake production output or describe the subset as full parity.

## Before editing

- Read this file and the relevant subsystem documentation before changing behavior.
- Check `git status -sb` and inspect the exact files you plan to touch. Preserve concurrent and
  user-authored changes.
- Keep changes within the requested scope. Coordinate before editing a shared file another worker
  owns.
- Use the pinned macOS reference commit `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f` when comparing
  behavior. Read it with `git -C <macOS-repository> show <commit>:<path>`; do not use an unrelated or
  dirty macOS working tree as evidence.

## Architecture and ownership

Read [ARCHITECTURE.md](ARCHITECTURE.md) before moving responsibilities between layers and
[docs/PARITY.md](docs/PARITY.md) before claiming a feature is complete.

- `src/Cotabby.App/` owns WPF presentation, tray lifetime, settings views, and the application
  composition root (`Hosting/AppHost.cs`). Views express user intent and observe shared services;
  they should not construct duplicate focus, input, or inference services.
- `src/Cotabby.Core/` owns portable values and deterministic policies. Keep platform calls out of
  this project so the same behavior can run against shared JSON fixtures on CI.
- `src/Cotabby.Inference/` owns model and endpoint boundaries. Keep mutable native model state
  serialized, cancellable, and independent per task: autocomplete, learning, and speech must not
  borrow one another's live runtime or prompt/session state.
- `src/Cotabby.Win32/` owns Windows UI Automation, global input, insertion, clipboard, credential,
  power, and other OS effects. Keep Windows quirks here and feed the rest of the app immutable
  snapshots through narrow Core contracts.
- `tests/` mirrors production responsibilities. Prefer pure policy tests and shared fixtures before
  adding UI or native-runtime coupling.

Explain subsystem ownership and data flow in type/file comments when introducing a non-obvious
boundary. Put a rule in Core when it is deterministic; put effects in Win32 or Inference; keep App
coordinators focused on sequencing, cancellation, and current UI state.

## Safety and privacy invariants

- Local inference is the default. No text, clipboard contents, screenshot, OCR, or notebook data may
  be sent to a service unless the user explicitly selects that task's endpoint and sees its
  destination. Endpoint settings are per task, and each request is bounded before transmission.
- Do not add cloud sync. The Windows port's local data stays local unless a distinct, explicitly
  authorized feature defines otherwise.
- Observe global keyboard input without taking focus. Suppress only a key the app has decided to
  consume. Treat IME preedit/composition as uncommitted input and do not translate, correct, or accept
  text against it.
- UI Automation is asynchronous and host-specific. Carry field identity, caret/selection state, and
  a freshness/version signal through asynchronous work; re-check them before showing or inserting a
  result. Fail closed for secure fields, unsupported controls, stale captures, and uncertain edits.
- Clipboard paste is an OS side effect. Preserve all available clipboard formats and handle a
  concurrent user copy safely; never silently claim rich clipboard restoration if only text was
  saved. A failed/uncertain replacement must not be retried against a different field.
- Bound and sanitize field, clipboard, OCR, dictionary, endpoint, and logging inputs. Normal logs
  must not contain typed text, prompts, selections, or credentials.

## Vocabulary and learning data

Preserve the macOS vocabulary contract when implementing learning storage: entry UUIDs identify
headwords, sense UUIDs identify meanings independently of their wording or order, and usage/example
UUIDs retain a particular encountered sentence or fragment with its exact text ranges and provenance.
Anki review/card identity is an independent relationship to those records. Do not flatten senses into
one translation string, regenerate stable IDs during edits, or treat a generated explanation as the
user's authored meaning. See the vocabulary models and porting gates in the linked docs. Storage
compatibility with macOS is not assumed; migrations must be explicit and tested.

## Build and CI boundary

`global.json` pins .NET SDK `10.0.402`. The current developer checkout keeps that SDK in the ignored
repo-local `.dotnet/` directory. On macOS, use it to run portable tests and the committed Swift
behavior verifier:

```sh
export DOTNET_ROOT="$PWD/.dotnet"
export PATH="$DOTNET_ROOT:$PATH"
dotnet --version
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-macos-parity.py --mac-repo /path/to/macOS-lingrove-checkout
```

The verifier reads the pinned macOS sources from Git and checks the same JSON cases; it does not
build the macOS app or run a model. If `.dotnet/` is missing in a fresh checkout, install SDK
`10.0.402` into that ignored directory so `global.json` can select it.

The current `.github/workflows/ci.yml` has two validation jobs: Windows x64 restores and builds the
full solution, then runs Core and Inference tests; macOS 26 runs the Core tests and the Swift parity
verifier against a sparse checkout of the pinned macOS source. The separate
`.github/workflows/upstream-baseline.yml` checks the original Windows fork at commit
`b1e4031c70aca1be21aae2c57b138fde5fc22bb1` with .NET 9 on manual dispatch. Its initial setup run is
recorded in the baseline note. It does not gate ordinary PRs; its result describes that old snapshot,
not the current .NET 10 tree.

CI is for dependency restore, compiling, and tests. It must not download GGUF model weights, create
installers or other downloadable packages, upload build artifacts, publish packages, or create a
release for each commit. Do not add a model-backed or network-dependent CI test. Ordinary package
manager restore needed to compile is allowed. Do not run the `Cotabby.Tools.*Smoke` projects as
routine macOS validation; they may need a Windows desktop, interactive editor, or downloaded model.
Native Windows UI Automation, IME, caret, insertion, clipboard, and GPU behavior still requires a
Windows host even when portable tests pass.

Report exact validation commands and outcomes. A passing compile is not proof of native UI behavior,
and a passing `build-for-testing` equivalent is not the same as executing tests.

## Completing a development round

The maintainer has authorized merging each completed round into `main` after independent review and
the required checks pass. Create a scoped PR, resolve review findings, verify CI for its final head,
and merge it without asking for another approval. Use GPT-6 Luna with maximum reasoning for delegated
implementation, independent review, and test work. Continue to exclude installers, downloadable build
artifacts and releases from routine rounds. A virtual machine is not a prerequisite for portable
logic, WPF compile checks or fake-driven tests; native desktop behavior still needs a Windows host.
