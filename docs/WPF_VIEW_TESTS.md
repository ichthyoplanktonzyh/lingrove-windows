# Compiled learning-view regression tests

`tests/Cotabby.App.Wpf.Tests` exercises the compiled production windows and their XAML on Windows
x64. It complements the portable App policy/ownership tests. The behavior reference remains Mac
revision `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

- `CotabbyTests/UI/InlineFeatures/LearningDictionaryCardTests.swift`: attributed literal definitions,
  bounded card body, component order/navigation and replacement of old content.
- `Cotabby/UI/InlineFeatures/LearningExplanationView.swift`: original-range example highlighting,
  separate meaning/Details, refresh retention, changed-input collapse, one header action.
- `Cotabby/Services/Presentation/LearningSentencePanel.swift`: one reusable nonactivating window
  whose current document owns its commands and explanation.

The Windows adapter uses a collection-owned STA thread, dispatcher and **plain WPF Application**.
It never constructs the product `App` or `AppHost`. Each test creates/closes its windows on that
thread; the fixture shuts down the Application after the collection. The internal friend assembly
exposes existing view entry points only to these tests. Production views still receive frozen Core
snapshots and emit immutable intents; they own no provider, global listener or editor authority.

The tests load real resources, show the actual windows, run layout, render an in-memory bitmap and
inspect the native `WS_EX_NOACTIVATE`/`WS_EX_TOOLWINDOW` bits. They check literal Unicode/markup,
exact highlighted ranges, response/example direction, bounded long-text scrolling with attribution
outside the body, Details retention/reset, target/snapshot-bearing routed button events, provisional
versus completed content, stale explanation rejection and hide/reopen cleanup. Rendering checks for
a nonempty visual; it is not a screenshot comparison or a font/caret alignment measurement. Routed
button events check wiring and receipts; they do not simulate physical mouse coordinates.

This coverage also fixes two view-level omissions: sentence-card action buttons now explicitly
decline keyboard focus, and hiding a reused card clears its retained captured/translated/title text.
These source invariants and the native style bits alone do not prove focus retention in another app.

Run on Windows after the solution build:

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj --configuration Release --no-build --no-restore --blame-hang-timeout 2m
```

The Windows CI job runs this suite; macOS cross-compiles it but cannot execute WPF. No clipboard
access, UI Automation, external editor, global input, model, endpoint, screenshot file, artifact
upload, installer or release is involved. Real editor activation, mouse hit testing, selection/copy,
IME, external-field freshness, mixed-monitor DPI, caret/ghost pixels, insertion acknowledgement and
CPU/GPU model execution retain their separate Windows-host gates in `PORTING_PLAN.md`. A green view
suite does not promote those parity rows to verified.
