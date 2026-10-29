# Expression assistance library integration

Reference: frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`.
The [pure policy](EXPRESSION_ASSIST_CORE.md), [instruction owner](EXPRESSION_ASSIST_INSTRUCTION.md)
and [session/view](EXPRESSION_ASSIST_SESSION.md) records map committed Mac sources and tests.
This checkpoint integrates those reviewed libraries; it does not register expression assistance
with AppHost or activate native editor capture, settings, triggers, shortcuts or insertion.

The instruction facade borrows the existing explanation runtime with independent request state
and retiring leases. Translation and completion retain their own runtime owners. Shutdown retires
and drains instruction work before disposing the explanation runtime, including no-delta cancellation
and service-disposal failure. Windows ICU supplies full Unicode lowercase without dictionary-key
rewriting. The WPF session owns temporary description text, composition/revision guards, debounce,
cancellation and candidate presentation; hidden/retired panel composition is cleared before reuse.
The pure trigger observer matches committed field snapshots after mutation arming. Native retained
host receipts and one-shot effects remain under independent implementation/review.

Independent reviews checked source and targeted controller/session/view tests, including repaired
shutdown and retired-IME-state cases. On Windows x64 10.0.19045, .NET SDK 10.0.403, the integrated
source at `a75a088` executed:

```powershell
dotnet build Cotabby.sln -c Release --no-restore --verbosity quiet
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --no-build --no-restore --verbosity minimal
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release --no-build --no-restore --verbosity minimal
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName!~ClipboardTextReaderTests' --verbosity minimal
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --no-build --no-restore --blame-hang-timeout 2m --verbosity minimal
```

The solution built with zero warnings/errors. Core passed 4799/4799, portable App 1235/1235,
noninteractive Inference 637/637 and compiled WPF 593/593. Tests disabled .NET telemetry and
clipboard reads. No model, endpoint or user's editor was used. Source-derived policy cases are
not actual Swift execution; component renders are not physical desktop/IME/accessibility acceptance.
Speech, collection actions, expression endpoints and full Unicode native trigger support remain gaps.
