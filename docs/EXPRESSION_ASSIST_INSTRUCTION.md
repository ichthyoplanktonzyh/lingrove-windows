# Expression assistance instruction owner

The frozen sources `Lingrove/Services/ExpressionAssist/ExpressionAssistService.swift` and
`Lingrove/Services/LanguageLearning/LearningExplanationService.swift` at
`35b7fbe5c788c5524936ade1dfb7c89a3a285b04` reserve a shared instruction request on the
explanation model controller. The Windows explanation owner similarly retires the previous
instruction lease as a new explanation or expression generation begins, and on profile,
eligibility or shutdown withdrawal. Cancellation is propagated to the borrowed native operation;
its facade drains before the one explanation runtime can be disposed. The owner retains its
existing local model admission and 60-second idle policy; translation and completion remain
independent. Cached explanation lookups do not reserve a generation lease.

The local instruction facade enforces general-instruction metadata, literal prompt tokenization,
actual model context budget, complete generation and a caller-specified raw-byte limit. Expression
requests use their own frozen prompt and 12,000-byte cap, with the source's default 768 and maximum
1,536 output tokens. The caller supplies the app-owned ICU lowercase function for result identity.
Only local explanation models are currently configured on Windows; no translation endpoint is
borrowed. This boundary is not activated in AppHost and owns no editor, UI or settings.

Tests use fake operations and source-derived examples, not a model. Physical model generation,
endpoint configuration, native editor/IME behavior and the expression UI remain unverified here.
