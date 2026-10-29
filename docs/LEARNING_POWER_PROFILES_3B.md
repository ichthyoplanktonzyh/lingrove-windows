# Learning power profiles

This component maps committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`:
`Lingrove/Models/LanguageLearning/LearningModelSettings.swift`,
`Lingrove/UI/Settings/Panes/Engine/LearningModelConfigurationView.swift` and
`LearningModelConfigurationView+Power.swift`, `Lingrove/Models/Settings/ModelContextSettings.swift`,
`Lingrove/Support/Onboarding/RecommendedSetupPolicy.swift`, and
`LingroveTests/Models/LanguageLearning/LearningModelSettingsTests.swift`. Historical fixture pins
are not changed. This is a component handoff; AppHost power delivery and pane insertion must be
integrated before claiming production support.

Each role retains manual, battery and plugged-in choices. A choice carries backend, local filename,
endpoint model and local instruction template; the role retains its address and credential. First
enable seeds both profiles from manual when no battery profile exists; disable restores manual.
The transient power source defaults plugged in and is not persisted. Settings projection selects
the effective destination and per-destination context/generation; dormant settings and the other
role do not enter an explanation snapshot. Translation endpoint sampling retains only temperature
and top-p. Existing single Windows endpoint preferences are the manual destination fallback;
other endpoint destinations default to 8,192 tokens and standard generation. The selected task's
existing endpoint consent and transport boundaries still apply. Referenced local files remain
protected and registered for task admission even when their power profile is dormant.

`LearningPowerControls` is a separate WPF component. It borrows the installed-model listing owner,
does not load weights, discover endpoints or read credentials, and allows a manual endpoint model.
It uses Windows controls at 96-DPI logical units with the app's styles. The pane must show the active
choice, lock manual model/backend/template edits while enabled, and retain active destination identity
when committing context or generation edits. The host must set `LearningPowerPluggedIn` before applying
both learning roles on every power change, independently of autocomplete's backend. Applying the
unchanged effective snapshot must leave active work alone; a changed snapshot retires old work.
Recommended setup respects explicit role endpoint and power choices and does not overwrite or download
their manual defaults.

Focused synthetic checks on Windows SDK 10.0.403: Core request/sampling 19/19, portable App settings
and explanation cancellation 20/20, and serialized WPF power/recommended setup 9/9. The WPF test uses
a synthetic settings owner and model names; no model, key, endpoint or user content is accessed.
It renders a component, not a complete integrated pane. No Mac screenshot was available. Physical
power notifications, keyboard/accessibility and integrated visual acceptance remain open.
