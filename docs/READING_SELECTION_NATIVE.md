# Windows selected-text reading boundary

Reference: frozen Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Services/LanguageLearning/LearningReadingSelectionReader.swift`,
`LearningReadingSourceReader.swift`, `LearningVocabularyContextReader.swift`, and
`Lingrove/Models/LanguageLearning/LearningReadingSelection.swift`. The Mac reader keeps
element/window and optional range or marker evidence privately, checks the original selected
occurrence and text, and does not copy to the clipboard. Context is optional and remains absent
in this Windows slice.

`Win32ReadingSelectionReader` borrows the application's focus tracker and keyboard hook; its
request-owned read-only lease has no write, clipboard, activation, or focus-restoration authority.
The existing `UiaTextEditEvidence` MTA owns a separate reading subscription and cloned native
TextPattern range. Capture is limited to the focused element of the foreground external process;
it checks the element and up to six ancestors for secure metadata before reading text. It requires
one nonempty selection, complete bounded text of at most 262,144 UTF-8 bytes, TextEdit pattern,
both composition event subscriptions, text/selection notifications and a successful fresh
`GetActiveComposition` query returning no range. Native registration success alone does not prove
provider event completeness. Unsupported, secure, stale, composing, or unknown inputs fail closed.

The receipt contains an opaque anchor UUID rather than a fabricated absolute UTF-16 offset. The
lease privately retains the original range; revalidation checks the same focused element, process,
foreground HWND and tracker epoch, both native range endpoints, exact UTF-16 text, and fresh clear
composition with unchanged event version. It never searches for matching text. Focus, input, text,
selection and composition events retire the receipt; allowed initial command key-up is scoped to
capture and one matching key. Request cancellation and retirement revoke permits between calls;
already-running provider calls cannot be interrupted, and disposal joins queued work and native
cleanup. Text and event payloads are not logged. [Microsoft's TextPattern selection](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-getselection)
and [range endpoint comparison](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-compareendpoints)
contracts describe the underlying operations.

This is an unactivated focused-selection slice: the application bridge, context enrichment,
pointer/icon, OCR and screen-region reading remain separate work. Read-only controls without
TextEdit evidence (including many static or browser hosts) remain unavailable; read-only alone is
not evidence that composition is clear. Hidden owned RichEdit tests establish only that fixture's
current UIA transport and do not establish physical IME, browser, user-host or visual acceptance.
On the development host, targeted WPF checks passed 11/11: three native owned-control cases and
eight fake lease/lifetime cases. The native cases include read-only RichEdit, selection at a second
identical occurrence, oversize UTF-8 rejection and a revoked queued capture. The fake cases cover
held validation, focus/foreground revocation, terminal notification replay, retirement/disposal
ordering and reentrant owner disposal. Subscribers added after retirement receive a replay until
disposal begins; disposal joins registered cleanup and callbacks, including late subscriptions.
