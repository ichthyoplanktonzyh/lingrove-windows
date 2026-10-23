# Voice catalog ranking and selection

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`SpeechVoicePolicy` in `Cotabby/Support/Speech/SpeechTextPolicy.swift`, actual
SpeechVoiceQuality/SpeechVoice declarations in `Cotabby/Models/Speech/LearningSpeechModels.swift`,
and `CotabbyTests/Support/Speech/SpeechVoicePolicyTests.swift`.

`SpeechVoice` is an immutable Core snapshot owned by a future system catalog adapter, with the
original exact tag, ID, display name, quality, preferred and novelty flags. Canonical string
equality/hash preserve Swift value semantics without normalizing retained text. It carries no
synthesizer handle or presentation object. `SpeechVoicePolicy.Voices` filters through the existing
language/script rule, then ranks exact before regional, ordinary before novelty, system-preferred
before other voices, higher quality before lower, then localized natural ID order. Equal comparisons
retain input order. The returned list owns its immutable snapshot.

`Select` first honours an exact compatible saved ID, including a novelty voice. A missing or
wrong-language saved ID falls back only within the requested language and skips novelty voices.
An all-novelty catalog can therefore display compatible choices while automatic selection returns
null. Names do not influence ordering, and saved IDs are not trimmed or case-folded.

The original last comparison calls Foundation localizedStandardCompare. This platform dependency
becomes a required IComparer<string> parameter. There is no default ordinal/culture approximation
and no Windows implementation yet. The future installed catalog owner must provide localized
comparison and map native metadata before this policy can be wired to a controller. This is the
platform seam of the original rule, not new product ranking.

`verify-speech-voices-macos-parity.py` compiles the complete original voice policy and actual
voice/language declarations. Nine cases adapt the original tests and compile their literal catalog;
103 extensions exercise every ranking tier, all palette languages, missing/mismatched/canonical
preferences, Chinese script tags, all-novelty catalogs, equal IDs and input permutations. Every
observation includes full ordered/selected voice values and the input catalog. The process pins
AppleLocale to en_US and asserts the active locale so source comparisons have a repeatable scope.
Expectations and every pairwise localized ID comparison were captured from that executable.

C# tests inject those observed comparisons only as the platform dependency; they assert the other
policy outputs against the original. This establishes ranking/selection given the source comparison,
not Windows collation or equivalence across user locales. Two additional cases verify canonical
value equality/hash, immutable list ownership, and that quality wins without consulting ID comparison.
Case IDs are locked in both runners. These source observations are not execution of Swift Testing.
All114 focused cases and full Core4417 passed with zero failed/skipped; Release solution cross-build
passed with zero warnings/errors. The original verifier passed locally and independently. Production review is clear; both
final-head CI jobs remain required before merge.

No installed inventory, Windows collation adapter, recognizer, speech settings/controller/provider,
audible playback or review media upload is connected. Native quality/preferred/novelty mapping and
voice availability remain explicit platform work. No model download, network test, audio file,
installer, release or uploaded artifact is produced.
