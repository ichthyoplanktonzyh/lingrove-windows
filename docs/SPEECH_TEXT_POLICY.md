# Spoken text and review audio freshness

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Original declaration/method | Windows Core counterpart |
| --- | --- |
| `Support/Speech/SpeechTextPolicy.swift` complete `SpeechTextPolicy` enum | `Speech/SpeechTextPolicy.cs` |
| `Support/Review/AnkiReviewPolicy.swift` `currentAudio` | `Review/AnkiReviewPolicy.Audio.cs` |
| `Support/Speech/SpeechExportPolicy.swift` `digest` | standard SHA-256 in the review helper |

This slice introduces no voice selection, settings, synthesis runtime, playback, file export, media
upload or automatic speech. The rest of the original speech-policy file (voice selection and local
language inventory) and the remainder of the export policy remain separate migration work.

## Responsibility and data flow

Learning cards eventually supply an immutable recognized `LearningUnit`; direct speech/review
consumers supply text. The stateless `SpeechTextPolicy` returns a bounded phrase or absence. It owns
no provider, UI state or task. Word/expression speech uses actual displayed surface rather than the
dictionary query. Pattern speech uses every ordered word/pronoun slot, including the words hidden
by visual ellipses. An empty pattern or a Foundation-trimmed empty word rejects.

Sanitization rejects raw input above8192 UTF-8 bytes before filtering. It drops original Foundation
control/format scalars except whitespace/newlines, collapses Swift-character whitespace and trims
with the existing Foundation set, then keeps at most1000 user-perceived characters. It never clips
by UTF-16 units or normalizes spelling. Foundation's historical U+200B exception matters: an inner
zero-width space remains, while edge U+200B is trimmed; BOM and other format controls follow their
original control classification. The existing grapheme adapter supplies Swift-style splitting and
prefixing. Unicode-version differences remain a native platform boundary.

The review helper compares each selected audio receipt's text hash to SHA-256 of the sanitized
expression or sentence. It retains the original empty fallback: unavailable sanitized text hashes
as the empty string. No selected kinds (nil or empty) is current; any missing/mismatched selected
receipt is stale. Hash comparison uses existing canonical string equality, so uppercase hexadecimal
still differs. This is only text freshness. It deliberately does not check provider fingerprint,
profile, endpoint, filename or remote media existence; the operation/controller must check those
independently before a write. No receipt is created or acknowledged by this helper.

## Evidence

`speech-text-v1.json` has47 observations from the actual complete SpeechTextPolicy enum and exact
original freshness/digest methods. The verifier also compiles the actual recognized-unit/word and
review model declarations. It does not rewrite exercised bodies or claim to execute an audio
provider. Native Swift JSON carriers retain decomposed/BOM scalars. Both runners lock the reviewed
case-ID matrix and count.

Eight rows adapt the four existing `SpeechTextPolicyTests` regressions; other rows explicitly name
source-policy extensions. Coverage includes actual surface/pattern pronouns, empty/malformed units,
controls/format scalars, U+200B/BOM distinctions, combining-space behavior, CRLF and noncharacters,
raw byte limits, grapheme clipping with emoji/decomposed text/flags, and selected/missing/changed/
empty audio hashes. Two extra .NET cases reject malformed UTF-16/undefined kinds and demonstrate
that this narrow freshness helper cannot establish provider/file identity. Inputs contain only
synthetic text/hashes, never voice files or keys.

Local validation passes all49 focused cases (47 source observations plus2 boundary facts), full
Core3644 and portable App269 without failures/skips, and full Release cross-build with zero
warnings/errors. Final-head Windows/Mac46-verifier CI remains required before merge. No model, voice device, socket, installer or uploaded build artifact participates.
