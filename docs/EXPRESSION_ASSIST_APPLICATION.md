# Expression assistance application composition

Reference: frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, particularly
`Lingrove/App/Core/LingroveAppEnvironment.swift`,
`App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`,
`Models/ExpressionAssist/ExpressionAssistSettings.swift`,
`Models/Settings/SuggestionSettingsModel+ExpressionAssist.swift` and the expression settings/panel
views. The [library checkpoint](EXPRESSION_ASSIST_INTEGRATION.md),
[event adapter](EXPRESSION_ASSIST_COORDINATOR.md) and
[native editor record](EXPRESSION_EDITOR_RECEIPT.md) retain their own evidence and limitations.

AppHost composes one expression feature before other inline keyboard subscribers, borrowing the
existing focus tracker, hook and explanation instruction generator. The same insertion gate is
injected into completion/emoji/macro insertion and the native expression editor. ICU case mapping
comes from the same lexical adapter. No second listener, translation runtime, notebook or endpoint
is created. Native expression activation is guarded to Windows 10 or newer; older hosts cannot
capture an expression through this adapter.

Learning settings expose an independent disabled-by-default expression switch, `??` trigger,
the shared target language and shortcut configuration. The shortcut defaults to unbound; recording
checks all configured apps, dispatch checks the active app, and saved conflicts remain visible as
other preferences change. The current implementation uses the local AI explanation model. It
does not inherit translation endpoint consent or credentials. Description, selection and bounded
neighbors remain local. A missing explanation model produces a fixed configuration message.

Detached settings keep hook decisions free of settings mutation and IO. Global pause/disable,
app exclusions, terminals, own windows and credential fields remain excluded; an explicit shortcut
may capture a selection. Temporary description input belongs to the panel. Opening it retires the
sentence preview and completion, and other inline captures yield while it is active. Settings and
focus changes revoke queued probes; their native permit is atomic and nonblocking. Shutdown drains
the expression feature, native editor and sentence preview before releasing explanation/native
services. An already-started COM call cannot be interrupted or given a bounded shutdown guarantee.
The tray sentence-preview command also retires its intent while expression input is active before
reading a field or presenting a window. Queued sentence captures recheck that exclusion before
their focus read and before publishing; already-started native reads cannot be interrupted.

This activates a limited implementation, not completed physical parity. TextEdit composition must
be positively clear; unsupported providers fail closed. Non-ASCII triggers are explicitly unsupported
by the native adapter. Real editor/IME/provider events, focus return, clipboard preservation,
SendInput, mixed DPI, accessibility and model-backed output remain native acceptance work. Speech,
favorite-row actions and expression endpoints remain unavailable. Native effects tests use fake
effects or hidden owned windows, never a user's editor. Application adapter tests use synthetic
events and denied commands, and never open an active input panel or load a model.

Integration review found and repaired two notification handoff races. A terminal invalidation
latch replays across native/bridge/UI subscriptions; the panel checks a live original-host
preactivation permit before showing and immediately before activating. Already-started native
effects still have a short check-to-call race. A write's consumption notification cannot erase
its pending verified/unverified outcome; failure feedback is shown only while the original host
window remains foreground. Settings/quit cancellation still retires the whole operation directly.
The tray preview and delayed capture also yield to expression input before reads or presentation.
The recovery notice is owned by this application adapter, remains nonactivating, and is replaced
or closed on new sessions, relevant settings and shutdown. Full Copy reports success then closes
its own notice after 900 ms. As in the frozen environment's `learningAvailability` closure,
integrated terminals remain excluded even when autocomplete is allowed there.
