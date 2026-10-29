# Expression assistance: pure policy checkpoint

Reference: macOS commit `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/Support/ExpressionAssist/ExpressionAssistPolicy.swift`,
`ExpressionAssistPrompt.swift`, `Lingrove/Models/ExpressionAssist/ExpressionAssistModels.swift`,
and `LingroveTests/Support/ExpressionAssist/ExpressionAssistPolicyTests.swift`.

`Core/ExpressionAssist` supplies trigger admission, bounded request construction, the distinct
expression prompt and complete-result admission. UTF-16 ranges must fall on .NET grapheme
boundaries and the existing Foundation composed-range map. Trigger classification uses the shared
Swift-character adapter; candidate identity uses the shared canonical comparer and Foundation
whitespace/control rules. The target and explanation languages use the existing learning language
catalog and the prompt uses the existing context budget. Result parsing requires an injected full
Unicode lowercase function; Windows supplies the existing system ICU adapter, without lexical-key
whitespace or apostrophe normalization. Unicode-version differences remain a platform verification gap.
The caller must clear selected text for a symbol-trigger request as in the source coordinator.

This is a library checkpoint, not an activated feature. The source's disabled-by-default settings,
shortcut conflict handling, saved host receipt, focused temporary IME input, 450 ms debounce,
generation cancellation, result display, restoration and single verified replacement still require
their respective Windows owners. Expression assistance uses the explanation model profile, with
an explicit endpoint disclosure for the description, selection and bounded neighboring text.
Speech and collection actions depend on separately activated owners; neither is implemented here.
No model, endpoint, native editor, physical IME or rendered UI is exercised by these pure tests.
