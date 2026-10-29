# Expression assistance event coordinator

The source authority is macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`:
`App/Coordinators/InlineFeatures/ExpressionAssistCoordinator.swift`,
`Models/ExpressionAssist/ExpressionAssistSettings.swift`,
`Models/Settings/SuggestionSettingsModel+ExpressionAssist.swift` and coordinator tests.

The app feature subscribes to existing shared focus and input owners; it does not start, stop or
replace them. Its committed snapshot defaults to disabled, `??`, an unbound Windows virtual key,
and unavailable. The host must publish a new revision for expression, explanation profile,
language, disabled-application, global enable and pause changes, and provide a cheap thread-safe
active-field shortcut conflict predicate. The recorder's complete conflict rules remain with the
settings owner. The synchronous hook path checks the immutable snapshot and cached focus only;
it consumes a matching permitted shortcut, ignores repeats after consuming, and posts capture to
the dispatcher. While a session owns input it passes keys through. Existing subscribers must honor
the shared `Suppress` flag, and root composition must subscribe this feature in the intended order.

Physical key characters are only mutation/probe hints. They never reconstruct IME text or become a
description. Matching uses a positively clear committed focus snapshot in the same field and epoch.
Ordinary mutations arm the focus publisher without extra reads; final punctuation starts at most
four refreshes separated by 35 ms. New mutations, settings, stop and successful opening retire old
probes. Async refreshes and retired sessions are joined at shutdown. While the native panel owns
keyboard focus, ordinary host focus notifications do not close it; otherwise loss invalidates the
session. Each factory invocation owns a distinct session and window, closed after that session
drains. The native editor still performs the independent capture, freshness and write checks.

This is an unactivated adapter. AppHost/settings composition, native editor bridge, physical
keyboard/IME/editor acceptance, shortcut conflicts across real applications, and native placement
remain separate work. Tests use synthetic hooks, snapshots, panels and editor receipts; they do not
inject input or run a model.
