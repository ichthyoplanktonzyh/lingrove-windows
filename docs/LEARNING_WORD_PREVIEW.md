# Completed-translation lexical and dictionary preview

This adapts stable Mac Lingrove at `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. A completed manual
translation now acquires independent lexical annotations. Double-clicking one of its words opens
that occurrence's expression/pattern or word in a nonactivating bundled dictionary card. The card
keeps source-owned senses and attribution separate, and supports component words and Back.

This is the completed target-translation/bundled-provider subset. Source-sentence double-clicks,
word fallback after recognition failure, system/imported/online dictionary providers and source
selection, contextual explanations, references, vocabulary/favorites and speech remain unported.
The card does not modify the editor or issue a model/network lookup.

## Mac authority and boundaries

| Pinned Mac source / tests | Windows adaptation |
| --- | --- |
| `App/Coordinators/InlineFeatures/LearningSentencePreviewCoordinator.swift`, `startUnits`, `cancelWork` and matching coordinator tests | `Core/LanguageLearning/LearningPreviewCoordinator.Units.cs` and state fields: independent task/lifetime/revision, completed-result-only input, 64-entry cache and failure isolation |
| `UI/InlineFeatures/LearningSentenceDictionaryText.swift`, `Support/LanguageLearning/LearningDictionaryWordPolicy.swift`, `LearningSentenceDictionaryTextTests.swift` | `LearningUnitDisplayPolicy.cs`, WPF `LearningPreviewWindow`: original text slices, neutral fixed-range underlines, double-click only on non-snapped glyph hits; exact occurrence targets |
| `Services/LanguageLearning/LearningDictionarySession.swift`, `LearningDictionarySessionTests.swift` | `LearningDictionarySession.cs`/`LearningDictionaryState.cs`: bundled-only results, query/target/document identities, component navigation, close and transient cache |
| `UI/InlineFeatures/LearningDictionaryCard.swift`, `LearningDictionaryContentView.swift` | `App/UI/Learning/LearningDictionaryWindow.xaml(.cs)`: literal source senses, component buttons, source/license links and attribution outside the definition scroll area |
| `Support/LanguageLearning/LearningDictionaryViewport.swift`, matching viewport tests | `LearningDictionaryViewport.cs`: first completed definition locks a 120–300 logical-unit viewport per query; loading/errors do not lock |
| `Support/LanguageLearning/DictionaryLinkPolicy.swift` | `DictionaryLinkPolicy.cs`: 4096-byte HTTP(S) destinations with a host and no credentials, opened only by an explicit activated link |
| `Services/Presentation/LearningSentencePanel.swift` | `App/Learning/LearningPreviewController.Dictionary.cs`: card ownership and exact capture validation; shared `LearningPanelPlacement.cs` preserves the previous Win32 no-activation/physical-coordinate rules |

Mac paths are under `Cotabby/` unless a test filename is shown; tests are under the corresponding
`CotabbyTests/` subsystem. Windows paths are under `src/` unless stated otherwise. The coordinator
partial is the existing preview owner's lexical responsibility, rather than another app-owned
service graph. Both recognition and lookup use the same lazy `LearningLexiconService` singleton.

## Data flow and retirement

Translation Ready may show immediately with lexical Pending. The separate recognizer never sees
stream prefixes and does not retain the translation engine lease. It receives only the complete
validated text and captured target language. A recognition error sets lexical Failed while keeping
the valid translation Ready. New requests, Cancel and disposal retire independent lexical work;
generation, unit revision, request ID, exact result and language must still match before publication.
Cache clearing also revokes old writes. Windows keys use exact original UTF-16 text, so canonically
equivalent encodings may perform extra recognition compared with Swift's canonical string keys;
this avoids reusing original occurrence offsets for a different encoding.

WPF runs concatenate to the unchanged original text. Only word slices carry double-click targets;
spaces/punctuation remain inert even when inside a neutral expression underline. Pattern slots
retain their separate word targets. Invalid grapheme/range correspondence or overlapping units
fails closed to literal text. WPF supplies visible character geometry; App revalidates the captured
field off-thread before opening and displaying a dictionary snapshot.

Pending double-click validation has its own revision: a slow earlier UIA read cannot overwrite a
newer click, and a close/document retirement revokes both pending opens and active lookups. Card
buttons carry captured target identity so an old occurrence cannot navigate a new one. Dictionary
queries never reread source text; word/Back navigation retains the same captured unit and context.

The session caches at most 32 query results with the Mac's 8MiB estimate of headword/definition
bytes and FIFO eviction. That estimate does not bound total managed memory or include every entry
field; the shared full resource remains independently retained/bounded. Misses are cached values;
unavailable sources are errors. The bundled-only card shortens the Mac resource error to its first
sentence, avoiding a promise that an unported system-word fallback exists.

## Evidence and remaining native checks

Core tests cover independent unit work, stale/canceled/cache-epoch results, cache eviction, lookup
navigation/context, exact occurrences, late close results, callback failures, literal display ranges
and external-link restrictions. `learning-dictionary-ui-v1.json` adds 19 source-policy extensions
(14 link and five viewport cases); they are observations of actual pinned Mac production code,
not claims that every case existed in an original test. The Swift verifier compiles the complete
link, content, insertion-safety and viewport policies and compares those committed observations.
The new driver calls only the external-link and viewport APIs; compiling their dependencies is not
evidence for content/path/HTML or insertion-safety rules in this 19-case corpus.
The viewport was implemented after capture; link checks were added during the Windows adaptation.

A Windows test joins a fake translator with actual ICU/resource recognition and the real bundled
dictionary session. It loads no model and performs no HTTP call. WPF compiles on the build runners;
those checks do not execute glyph hit testing, keyboard-focus retention, scrolling, clipping,
Arabic rendering, multi-monitor DPI or card placement on a Windows desktop. Those native checks
remain required. No installer, artifact upload, downloadable package, release or model download
is produced for this checkpoint.
