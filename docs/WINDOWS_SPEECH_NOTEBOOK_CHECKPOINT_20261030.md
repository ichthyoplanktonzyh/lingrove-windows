# Shared speech and notebook learning checkpoint

Target: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`. Current notebook explanations/dictionary,
Windows speech providers/settings/cards and Anki audio/template controls have been independently
reviewed and joined to AppHost. One canonical Store and one speech controller serve their borrowers.
Speech uses its own DPAPI file. Normal shutdown drains windows and Review before providers and Store.
Startup attachment does not enumerate voices, load models, start helpers or access credentials.

Windows x64 10.0.19045, .NET SDK 10.0.403, source through `617cd82`:

```powershell
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:COTABBY_CLIPBOARD_TEST='0'
$env:LINGROVE_TEST_SYSTEM_SPEECH='0'
dotnet restore Cotabby.sln -v:q
dotnet build Cotabby.sln -c Release --no-restore -v:q
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --no-build --no-restore -v:q
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release --no-build --no-restore -v:q
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName!~ClipboardTextReaderTests' -v:q
./scripts/test-wpf-dev.ps1 -NoBuild -NoRestore -Logger 'console;verbosity=minimal'
```

Build: zero warnings/errors. Core **4882/4882**, portable App **1248/1248**, noninteractive Inference
**669/669**, compiled WPF **898/898**. The six Windows projects use SDK target 19041; portable Core,
Inference and App boundary projects remain portable. Controlled/fake providers and isolated stores
exercise the UI and effects; no real model, endpoint, user clipboard/editor or audible playback was used.

A subsequent native-authorization repair, `02595e3`, makes the final reading permit directly observe
the native receipt's atomic Active flag, even before its invalidation callback reaches the next layer.
Independent source review checked the nonblocking predicate and paused-notification regression.
The solution rebuilt without warnings/errors; serialized lease/presentation/system-provider tests
passed **32/32**. It changes no prompt, storage format or ordinary no-permit speech request.

The [native helper build record](SPEECH_WINDOWS_HOST_BUILD_20261030.md) separately records actual CPU
compilation and 14 model-free binary checks. Its copied development executable is available locally;
this is not model-backed generation, hardware/voice quality, physical Windows acceptance or a release.
Screenshot/pointer/context expansion, CLI, broader localization/onboarding and diagnostics continue
as separate complete workflows. Final-head remote CI remains required before merge.
