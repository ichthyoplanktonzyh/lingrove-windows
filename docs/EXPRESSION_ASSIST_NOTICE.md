# Expression assistance recovery notice

Reference: frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, `Lingrove/Services/Presentation/ExpressionAssistPanelController.swift` (`showFailure`, `copyFailure`, `closeFailure`) and `Lingrove/Support/ExpressionAssist/ExpressionAssistLayout.swift`. The window presents the caller's failure message and immutable candidate, with explicit Copy and Close. Candidate text supports mouse selection without taking keyboard focus; copying a selection uses its explicit context-menu action. The full-candidate Copy reports success for 900 ms and remains retryable on clipboard failure. A null candidate produces a static unavailable message and Close. Construction and showing do not touch the clipboard or the host editor.

`ExpressionAssistNoticeWindow(message, candidate)` borrows no host, Store or session. `ShowAt(ScreenRect)` accepts the already captured physical-pixel caret and uses `LearningPanelPlacement` for monitor/work-area/DPI placement and `WS_EX_NOACTIVATE`. Its caller must check that the original host remains foreground before showing, and must close/replace this window on new sessions, settings changes and shutdown. This component alone does not activate the application or connect the existing expression session's failure path. It uses shared WPF `Theme.xaml` and `WindowTheme` resources with system high-contrast brush fallback; 96-DPI units govern layout. A long candidate scrolls while Copy, Close and clipboard failure status remain visible.

STA control tests use an injected clipboard action and exercise no real clipboard or user editor. They check explicit full/selected Copy, no copy on show, failure feedback, unavailable state, native no-activate window style and timer/close, plus optional light/dark/simulated-high-contrast offscreen renders. Actual Windows high contrast, per-monitor DPI, physical pointer/selection, screen reader and host foreground/focus behavior still require desktop acceptance.

```powershell
dotnet test tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj -c Release --filter 'FullyQualifiedName~ExpressionAssistNoticeWindowTests|FullyQualifiedName~ExpressionAssistViewTests'
```
