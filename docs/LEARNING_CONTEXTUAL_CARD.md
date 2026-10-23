# Contextual word and expression card

This adapts the stable Mac `LearningDictionaryCard.swift`, `LearningExplanationView.swift`,
`LearningDetailPanel.swift` and `VocabularyCaptureFactory.swift` at revision
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. It connects the
[contextual query session](LEARNING_CONTEXTUAL_QUERY.md) to completed writing-translation lookup.

| Windows boundary | Responsibility and data flow |
| --- | --- |
| `App/Learning/LearningPreviewController.Dictionary.cs` | Receives a double-click's exact unit/document/anchor; constructs the unit input on a worker; revalidates the captured editor; lends the existing app explanation service to the Core session. |
| `LearningPreviewController.cs`, `.Explanation.cs` | Shared capture freshness and intent retirement; sentence-card commands use the same atomic expected-snapshot checks. AppHost still owns the single independent explanation runtime/service. |
| `UI/Learning/LearningDictionaryWindow.xaml(.cs)`, `.Contextual.cs` | Renders the two pages, one header action, meaning, exact example ranges and collapsible Details. Emits immutable target/snapshot intents without reading UIA or generating. |
| `LearningDictionaryCardPolicy.cs` | Portable literal-range and reciprocal example projection. It never changes the explanation input or substitutes dictionary definitions for captured text. |
| Core `LearningDictionaryState.ExplanationLanguage` | Frozen response-language label from the borrowed service, published outside the dictionary lock alongside its current explanation snapshot. |

Explicitly opening a unit defaults to its contextual meaning. A configured model starts one local
request; an unconfigured role exposes Configure and leaves Dictionary available. Selecting a
component or Back uses the same behavior. Tabs do not generate: dictionary content stays lazy,
and stopped/completed explanation work stays stopped/completed. One header action can stop,
refresh or configure from either page, as on Mac; changing the page is a separate intent.

Before auto-generating, the App adapter runs full UIA freshness checks both before and after ICU
input construction. Only the complete translation is used. Component/Back/navigation and explicit
generation are also revalidated off-thread before admission. Dispatcher commits recheck capture
reference, intent, target, exact source/result/options, unit membership and navigation revision.
Render jobs additionally require the exact current immutable state. Stop and Close act immediately
against the displayed target/snapshot. A later navigation or close invalidates older queued work.
Failed freshness retires the current card without waiting for its regular polling interval.

The exact original context appears in the input language with only the captured UTF-16 occurrence
highlighted. Meaning and Details use the configured response direction. When the example covers
the whole translated sentence, a separate UI-only value displays the reciprocal original-language
line, matching Mac; a shorter example cannot reuse the translation of the whole sentence. That
original source never enters the explanation input/provider. Missing example projection uses
Mac's general Meaning fallback with context None rather than claiming a sentence was captured.

The dictionary page keeps source attribution separate from the AI model label. Details collapse
on changed input; refresh/stop/failure retains the previous complete answer. The body uses the
existing 120–300 DIP query/page viewport policy, while tabs and the header stay outside scrolling.
The title has a three-line bound. Configure first retires the preview before opening Learning
settings. Closing clears retained literal content. No editor insertion or notebook adoption occurs.

Portable App tests check repeated/discontiguous/overlapping ranges, raw decomposed Unicode,
invalid grapheme ranges, context-None behavior and the whole-sentence-only reciprocal line.
Existing Core contextual/session and six Swift fixture suites keep their documented scope.
Full compilation checks WPF/XAML types, not pixels, UIA timing or actual mouse interaction.
The separate [Windows WPF view suite](WPF_VIEW_TESTS.md) runs the compiled windows, layout and
in-memory rendering with synthetic snapshots; it checks routed-command identity and native style
bits while keeping external-editor behavior as a separate gate.

Native Windows verification still needs a host walkthrough: no activation/focus theft, context
clicks, Details/scroll/resize, DPI/work-area placement, field changes, IME and real model output.
Mac's text-selection/copy interaction is also still unported: these readonly WPF text blocks do
not provide it. A selectable readonly document control needs its own focus/interaction validation; WPF exposes selection on [FlowDocumentScrollViewer](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.flowdocumentscrollviewer.isselectionenabled?view=windowsdesktop-10.0).
Source-word fallback, source-text/reading/selection/OCR capture, dictionary source routing and
references, speech, durable vocabulary/favorites, adoption, localization, telemetry and explanation
endpoint transport remain separate migration work. No package or model is produced/downloaded by CI.
