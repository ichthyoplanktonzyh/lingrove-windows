# Captured usage identity

This stage ports stable Mac vocabulary usage/capture values and the pure identity/admission rules.
It introduces no notebook entry/sense graph, durable schema, database, save command, card Save UI,
merge/move transaction or import/export format. Those remain next boundaries. Mac Codable/SQLite
compatibility is not assumed, and a passing capture check is not imported-notebook validation.

The reference is `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`:

| Mac source | Windows responsibility |
| --- | --- |
| `Models/Vocabulary/VocabularyModels.swift`: meaning origin, meaning, example, capture, selection and explanation destination | `Core/Vocabulary/VocabularyCaptureModels.cs`: immutable values and explicit IDs; a future sense/store owns usages, while cards lend detached save intents. |
| `Support/Vocabulary/VocabularyPolicy.swift`: normalized, entryKey(capture), exampleKey, captureKey, validate(capture), valid(range), private key framing | `Core/Vocabulary/VocabularyCapturePolicy.cs` and `Core/Text/FoundationTextRanges.cs`: no IO, model or sense inference. |
| `Support/LanguageLearning/LearningExplanationPolicy.swift`: input(capture) and isValid(input) | New `LearningExplanationPolicy.Capture.cs` projection and the existing validation gate. The writing InputFactory reuses the same usage receipt without constructing a usage/UUID/timestamp. |

Usage IDs remain stable during edits. Only explicit `CopiedWithNewId` creates a new usage identity,
retaining all other values: encountered text, ordered exact UTF-16 ranges, sentence/fragment scope,
translation and language, translation variants, contextual meaning/language/authorship, app identity,
created date and complete explanation records. Constructor and `with` list assignments detach
mutable drafts; published lists reject mutation. Meaning wording/origin is separate from identity.
`SourceApplicationId` is the Windows counterpart of Mac `appBundleIdentifier`; no new platform
capture adapter populates it in this slice, and it never enters explanation input or a prompt.

Entry keys hash language, target kind and NFC/whitespace-collapsed expression, retaining case.
Dictionary form is not identity. Usage keys hash **original UTF-8** text, ordered UTF-16 spans and
completeness, excluding UUID, translation/annotation language, meanings, app and explanation records.
Parts are length-framed in UTF-8 before SHA-256 to prevent delimiter ambiguity. Value equality
follows Swift canonical string equality but does not normalize the original bytes used by usage keys.
Changing sentence/fragment or a clicked occurrence changes the usage key; refreshing its annotation
does not. Absent example has `no-context` exampleKey and a distinct `standalone` captureKey suffix.

Capture validation preserves the Mac subset: expression must be nonempty after key normalization
and at most 262,144 original UTF-8 bytes. Present examples must have nonempty text within the same
byte limit and valid original composed-sequence ranges. This rule does not invent a nonempty,
nonoverlapping or sorted-range requirement: the Mac capture validator permits an empty range list,
duplicates and reversed ordering when each individual range is valid. Acquisition and full document
validation enforce their own contracts. Malformed .NET UTF-16 and unsupported target enum values
have no Swift counterpart and fail closed. It is not a live-editor or sentence-boundary proof.

Foundation whitespace for content and Swift Character whitespace for entry keys are distinct:
U+200B is trimmed by the former, retained by the latter. The new executable corpus also exposed
NSString's separate CR/LF composed-range boundaries within CRLF. `FoundationTextRanges` adds that
boundary to .NET StringInfo offsets for vocabulary and explanation evidence. Readonly visual
selection continues to use whole CRLF/emoji/combining graphemes through `LiteralTextSelectionMap`.
No blanket normalization of line endings or shared UI-selection change is introduced.

`fixtures/parity/vocabulary-capture-v1.json` contains 63 source-policy probes. The eighth Swift
verifier compiles actual pinned usage/capture models, exact Kind declaration, identity/validation
methods and capture-to-explanation projection/validation. It excludes the unported entry/sense/review
graphs instead of faking their behavior. Language/catalog, budget/estimator and error dependencies
are original source too. Observations include normalized bytes/hash, exact keys, capture validity,
explanation validity/receipt and every input field (target, dictionary form, language/kind, context,
ordered ranges and completeness). Large repetitive text uses a text spec, with exact byte count and
SHA-256 to keep the fixture compact. No expected result is produced by the C# implementation.

Ten standalone cases cover edits versus explicit ID copying, detached lists, canonical equality
with raw preservation, annotation-independent keys, invariant decimal framing, .NET-only invalid
values and the evidence/visual CRLF distinction. Existing writing-capture fixtures still check the
transient factory. Store save/translation-variant merging, authorship protection on regeneration,
alias resolution, destination ambiguity, persistence and restart are not established by value tests.

```sh
DOTNET_ROOT="$PWD/.dotnet" .dotnet/dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj --configuration Release
python3 scripts/verify-vocabulary-capture-macos-parity.py --mac-repo /path/to/macOS-lingrove
```

No endpoint, model download, app launch, user library write, package or uploaded artifact is part
of this stage. Native acquisition, storage and the remaining platform gates are still unverified.
