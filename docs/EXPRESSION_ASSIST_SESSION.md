# Expression assistance session and WPF boundary

The reference is `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`, particularly
`App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`,
`Services/Presentation/ExpressionAssistPanelController.swift`,
`UI/InlineFeatures/ExpressionAssistCandidateRow.swift` and coordinator/panel tests.
The new WPF view provides a temporary native description field, selected candidate, base form,
hint, loading, clarification and error states. Enter searches or inserts, Tab inserts, arrows
select and Escape cancels. The session debounces for 450 ms, invalidates stale requests and
checks the saved editor session before generation and presentation. A symbol cancel requests
one verified removal of its marker; a shortcut cancel does not edit its selection. An uncertain
insertion exposes a copy-only failure instead of retrying a write.
The pure trigger observer arms only after a text mutation, expires after 500 ms and requires a
matching committed snapshot in the same focused field. It reports whether final punctuation
warrants a probe; the future event owner must schedule and cancel the source's four 35 ms refreshes.

The editor is a borrowed contract. Capture occurs with the panel HWND registered before activation;
the platform owner checks actual panel ownership, exact retained host evidence, authorized return,
two fresh host observations, the shared insertion gate and one write. The panel supplies an atomic
keyboard ownership snapshot, not a synchronous dispatcher callback. Native capture and effects are
still under separate implementation and review; this session is not registered with AppHost.
Current native scope cannot capture non-ASCII symbol triggers and displays an explicit instruction
to use ASCII symbols or the shortcut; full-width `？？` and other Unicode triggers remain a gap.
Speech and vocabulary row actions are not connected. No endpoint is configured for this role.

WPF composition start/update retires prior requests and blocks commands; a committed TextInput
clears that state only after the control applies its text. A cancellation with no completion stays
closed to commands. These are component checks, not physical IME proof. The caret anchor uses the
existing physical-pixel learning panel work-area placement; multi-monitor, mixed-DPI placement,
outside-click behavior, focus restoration, host editing, accessibility and real
model inference require native acceptance. Rendered component evidence uses a 96 DPI test window;
the source-derived fake tests do not operate a real editor or load a model.
