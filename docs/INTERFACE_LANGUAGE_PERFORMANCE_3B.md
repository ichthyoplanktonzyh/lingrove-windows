# Interface language and performance: current work

Source: macOS `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Models/Settings/InterfaceLanguage.swift`, `InterfaceLanguageSettingsModel.swift`,
`Support/Settings/InterfaceLanguageNormalizer.swift`, `InterfaceLanguageSettingsStore.swift`,
`UI/Settings/Components/Controls/InterfaceLanguageSectionView.swift`,
`UI/Settings/Panes/PerformancePaneView.swift`, `Models/Runtime/Metrics/ModelRequestMetrics.swift`,
`PerformanceMetricsStore.swift`, and the corresponding normalizer/model/metrics tests.
The three byte-preserved `resources/InterfaceLanguage/*.xcstrings` are copied from that commit's
`Lingrove/Resources` under this repository's upstream AGPL attribution. Git blob IDs are Learning
`6d146ecd31d5b2d5b00e1a044ab42ae8d3a2ca14`, Localizable
`27a159497d6279e83c95ba2aa5561a8e4f70b39d`, Vocabulary
`a67576c8344413d85403234615bc70f20b81c0d2`.

The 15 native-name choices and tag normalization follow the reference. The app-only choice is a
raw nullable value in the existing Windows settings owner; unrecognized values survive until an
explicit selection or reset. At startup `InterfaceText.Initialize` freezes the selected supported
locale, falling back to the Windows UI locale and then English; changing the preference requires
restart and does not apply generation settings. Only explicitly chosen UI keys are translated;
missing Windows strings fall back to English. Settings headings/rows, initial welcome XAML and
matching static labels in the canonical notebook and its editors use this lookup. Live bindings,
editable controls and user-provided text are never passed to it. The Windows wording for Follow System replaces Mac
catalog translations that refer specifically to macOS. Restart Now borrows the application-lifetime
callback and explicitly disables itself when none is attached. Settings, welcome and canonical
notebook windows inherit right-to-left flow for Arabic; other surfaces and native accessibility
still need separate verification. Full application-wide string coverage and physical desktop checks remain open.
The Windows first-use flow adds two inert source-derived completion/learning examples, a platform-specific accessibility explanation and a separately
versioned furthest-step resume marker because recommended and manual templates use distinct pages.
The application startup must request resume; manually reopening starts at the welcome page. It
still lacks the source's interactive dual-product showcase and complete readiness presentation;
recommended setup itself is already separately implemented.

One injected PerformanceMetricsStore serves speech and the settings pane. Tracking defaults off;
changing it saves only preferences and does not reconfigure a model. The existing 100-entry ring
survives general reset, while the opt-in resets. The pane offers source filtering, trend, stages,
clear, and a visible-only Windows process CPU/working-set sampler. CPU may exceed 100%; GPU and
helper-process totals are explicitly unavailable rather than zero. The 3b expression-assistance
source is supported without changing the old 43b selectable-source fixture. Speech emits content-free
request events through a borrowed callback and records through the same history. Completion emits
metadata from the actual admitted local model or frozen endpoint configuration, using a bounded
single-dispatch history collector; AppHost must attach and drain those events. The writing
translation coordinator reports actual cache-miss generation and validated-result stages through
the same collector, which its controller borrows. Explanation service and expression-assistance
generation also publish content-free observations with their frozen model; AppHost attaches these
to that collector. Reading translation and OCR observer wiring, child-process/GPU counters, and native
resource sampling comparison remain open. AppHost must construct and inject this one history and a
content-free log sink, and perform early language bootstrap before any window construction; the
present component does not claim production composition until that glue is installed.

Validation in this candidate: Release solution build 0 warnings/errors; Core targeted language,
completion, translation and explanation 82/82; portable App explanation/telemetry/onboarding
109/109; fake endpoint completion 11/11; serialized WPF language/history, creation/import,
canonical entry/category and speech settings 36/36. Offscreen creation light/dark and simulated
high contrast, Simplified Chinese and Arabic including narrow layout were captured under
`%TEMP%/interface-language-renders` and inspected. The helper applies the actual WPF ancestor
transform for RTL; this is not physical desktop or assistive technology acceptance. The fixed
examples and fake transports do not read a user editor, call a real endpoint or play audio.
