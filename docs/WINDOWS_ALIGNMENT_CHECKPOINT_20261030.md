# Organization and review integration checkpoint

Target: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`. Windows code through
`e2639e0` integrates independently reviewed meaning/classification, usage-pattern editing,
independent review cards and batch selection/preparation/sends. Later documentation changes
do not change that executed source. AppHost borrows one canonical Store and the existing
Review Feature/client; startup remains inert. Physical acceptance is consolidated for the
maintainer after the functional work, rather than requested at each internal checkpoint.

The meaning editor saves its detached semantic/classification graph in one acknowledged
transaction. The v4 catalog and its source notices retain exact committed bytes. Independent
cards and batch rows preserve IDs, explicit conflicts/recreation, pending sends and receipts.
Windows retains already confirmed batch rows when a deck application is stopped; the module
documents that correction to the Mac source's cancellation status behavior.

Executed on Windows x64 10.0.19045 with .NET SDK 10.0.403, telemetry disabled and
`COTABBY_CLIPBOARD_TEST=0`:

```powershell
dotnet build Cotabby.sln -c Release --no-restore -v:q
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release --no-build --no-restore
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName!~ClipboardTextReaderTests'
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --no-build --no-restore --blame-hang-timeout 2m
```

Build: zero warnings/errors. Core **4856/4856**, portable App **1244/1244**, noninteractive
Inference **650/650**, WPF **807/807**. An initial relational-probe failure exposed the deliberate
3b payload extension against historical 35b JSON; the historical row encoder now preserves that
original probe while refusing to discard new guidance. Current SQLite JSON structures, reopened
graphs and timestamp bits are checked independently. The original corpus and source pin remain
unchanged. The encoder boundary and preference-reset integration received independent source review.

These checks use synthetic services and isolated files/owned controls. No live model, real Anki,
network request, user notebook, user editor or user clipboard was exercised. They do not establish
physical input, IME, Shell, DPI/high contrast, screen-reader, Anki foreground activation or model
quality. Required final-head remote CI remains a separate merge gate. Readonly-reading foundations
are included but its production flow, graph, export/backup, OCR/CLI and speech have their own
subsequent integration/acceptance records; they are not completed by this test count.
