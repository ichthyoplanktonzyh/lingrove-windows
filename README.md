<p align="center">
  <img height="150" alt="Lingrove whale logo" src="docs/images/logo.png" />
</p>

<h1 align="center">Lingrove for Windows</h1>

<p align="center"><em>Local-first autocomplete and language learning while you write.</em></p>

<p align="center">
  <a href="LICENSE"><img alt="AGPL v3" src="https://img.shields.io/badge/license-AGPL--3.0-blue.svg" /></a>
  <img alt="C# / .NET 10" src="https://img.shields.io/badge/C%23-.NET%2010-512BD4?logo=dotnet&amp;logoColor=white" />
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-lightgrey" />
</p>

Lingrove has two equally core capabilities: autocomplete while writing and language learning while
writing. This repository adapts [Lingrove for macOS](https://github.com/ichthyoplanktonzyh/lingrove)
to Windows/WPF. The behavior, information architecture and presentation-state reference is the
committed [Mac baseline](docs/MAC_LATEST_BASELINE.md); Windows controls, typography and window
chrome retain their native character.

The port builds on [Msde-7/cotabby-windows](https://github.com/Msde-7/cotabby-windows), which in turn
builds on [Cotabby](https://github.com/FuJacob/cotabby) by FuJacob, jam-cai, akramj13 and contributors.
Their attribution and the AGPL-3.0 license remain in place.

## Current implementation

- **Autocomplete:** local/explicit endpoint inference, streaming and predictive typing, caret ghost,
  word/phrase acceptance, app and optional clipboard context, emoji, macros and spelling support.
- **Writing and expression assistance:** sentence previews and translation, explanations,
  acknowledged range replacement, original-host return, candidate speech and canonical favorites.
- **Vocabulary notebook:** a single current-format Store connects capture, creation/import, editing,
  senses, meanings, categories, usage patterns, images, graph navigation, export and backup/restore.
- **Review and speech:** independent Anki card preparation and batch operations, mappings and audio
  preparation; separate system/local/endpoint speech ownership and settings.
- **Reading and dictionaries:** explicit selection, OCR-first pointer reading with strict UIA fallback,
  and screen-region reading; bundled/local MDict sources and explicit online/website actions.
- **Setup and presentation:** task/model assignment, recommended setup, a seven-scene Home/Welcome
  walkthrough, interface-language selection, shared opt-in performance history and local CLI mailbox.

These are implementation checkpoints, with automated and component-render evidence. They are not a
claim that every editor, IME, speech provider or hardware configuration has passed native acceptance.
See [feature evidence](docs/PARITY.md) and [Windows host acceptance](docs/WINDOWS_HOST_ACCEPTANCE.md).

Remaining work includes selection visual-context enrichment, language/platform recognition limits,
some bitmap/drop/dictionary-resource variants, custom review layouts and full interface-language
coverage. Unsupported states stay explicit. Word, Chrome and Codex are the maintainer's final unified
host acceptance order; physical DPI, focus, keyboard, clipboard, OCR, models and audio remain distinct
from compilation and isolated tests.

## Privacy and local data

Local generation is the default. A service endpoint is an explicit per-task choice with destination
disclosure and bounded requests. Screen pixels remain local; recognized text follows the selected
reading task's destination. Cloud sync is outside this port.

The Windows runtime keeps existing `Cotabby` namespaces, assemblies, executable names, IPC and
settings/storage identifiers. User-facing Lingrove branding does not rename those contracts. The
current notebook uses a fresh, versioned file under the existing Cotabby data root; old files are
preserved without implicit conversion or cross-OS admission.

## Build and test

Use a Windows x64 development host and the .NET SDK selected by [global.json](global.json)
(`10.0.402`, with its recorded patch roll-forward policy). The [Windows development guide](docs/WINDOWS_DEVELOPMENT.md)
covers read-only prerequisite checks, runtime setup and source reference access.

```powershell
./scripts/check-windows-dev.ps1
dotnet build Cotabby.sln --configuration Release
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj --configuration Release
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj --configuration Release
./scripts/test-wpf-dev.ps1 -Configuration Release -NoBuild
```

The WPF wrapper serializes tests sharing the desktop. Tests use synthetic/owned resources; actual
user-host acceptance is separate. On macOS, portable Core/App tests and committed Swift verifiers
check the pinned sources and fixtures without building or running the Mac app.

The app executable retains its current name:

```text
src/Cotabby.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Cotabby.exe
```

The independent local speech helper has its own native build and runtime path; an ordinary app build
does not download it or model weights. The app reports unavailable providers rather than generating
sample output. Routine CI builds/tests source and pinned parity contracts; it creates no installer,
downloadable application package, model download, artifact upload or release.

## Repository map

| Location | Responsibility |
| --- | --- |
| `src/Cotabby.App` | WPF presentation, tray, settings, onboarding and composition |
| `src/Cotabby.Core` | Portable values, policies and orchestration |
| `src/Cotabby.Inference` | Independent model/endpoint boundaries |
| `src/Cotabby.Win32` | UI Automation, input, insertion, clipboard and Windows effects |
| `resources` | Pinned learning/language resources, approved brand masters and attribution |
| `tests` | Portable, boundary, inference and compiled WPF/component checks |

Read [AGENTS.md](AGENTS.md), [ARCHITECTURE.md](ARCHITECTURE.md) and the relevant module handoff
before contributing. Mac reference resources are read from committed Git snapshots, never a dirty
Mac working tree. Historical module evidence retains its own pin until explicitly upgraded.

## License and attribution

This project is licensed under [GNU AGPL-3.0](LICENSE), consistent with its upstream projects.
Preserve [third-party licenses and notices](THIRD_PARTY_LICENSES.md). Libraries, dictionaries, emoji
data and model weights have their own terms; consult those notices before redistribution.
