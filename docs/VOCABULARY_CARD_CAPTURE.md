# Durable captures from writing cards

`Core/Vocabulary/VocabularyCaptureFactory.cs` now adapts the writing branches of Mac
`VocabularyCaptureFactory.preview`, `.unit`, `.component` and `VocabularyPolicy.example`. It
turns a completed immutable writing preview into a detached save intent. The next UI slice will
connect that intent to the existing shared Store; this round does not expose a Save button.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

The factory owns no editor, database, model or network. It borrows the existing lexical and sentence
fact providers, preserving source text and exact UTF-16 ranges. Word and sentence facts remain a
Windows platform boundary; the portable corpus supplies only observations from actual Mac
NLTokenizer. Existing Windows ICU tests cover their previously documented native-language subset.

For a writing preview, the target-language translation is the expression and the original text is
its meaning. A one-word target has no invented example. A sentence captures its exact text/range,
full original translation and response language. For a unit inside a sentence, a sentence-level
translation is retained as example translation only when it covers that entire example; it never
becomes the definition of the subexpression. Contextual unit meanings remain empty until an actual
captured/generated/user-authored annotation exists.

A new example owns a new UUID, timestamp and the supplied frozen source-application identity.
A component reuses the encountered example's UUID, time, provenance and translation, while mapping
its exact occurrence into the local sentence window and clearing the parent contextual meaning.
Save policy later prevents one example UUID from owning different entries. No current focus or
first text match is used to relocate a repeated word.

`LearningExplanationInputFactory` now projects preview/unit inputs from this same capture factory,
matching Mac's capture → explanation-input order. Usage identity/time, meaning and translation never
enter the model's transient explanation input. Its existing component/unscoped input adapters stay
unchanged. This removes duplicated sentence/word acquisition logic; corrupted provider facts still
produce the same fixed failures, with no backend or typed text in the error.

`vocabulary-card-capture-v1.json` adds 33 actual full Mac capture observations over the same fixed
inputs as the prior explanation-input corpus, with an explicit application provenance value.
The verifier reuses original declaration inputs and the full Mac factory, and changes only driver
observation fields. Before adopting the results, every tokenizer fact and explanation-input value
was checked against the earlier corpus and remained unchanged. Random UUID/time fields are not
used as equality expectations; three standalone tests cover fresh identity/time, component retention
and a standalone word's lack of an invented encounter. The old 33 explanation-input fixtures and
provider failure tests continue to exercise the compatibility boundary.

Only the existing manual caret-sentence writing flow is represented: no reading source, no
selection target and no OCR crop boundaries. Reading/selection/context acquisition, original-text
fallback, UI favorite actions and notebook navigation remain migration work. No fake dictionary,
model result, production tokenizer or notebook is supplied by the new factory.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-card-capture-macos-parity.py --mac-repo /path/to/macOS-lingrove
python3 scripts/verify-learning-capture-macos-parity.py --mac-repo /path/to/macOS-lingrove
```
