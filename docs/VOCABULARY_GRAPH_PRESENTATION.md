# Original graph preferences and presentation

Reference: stable Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.
This library adapts existing preferences and per-window state; it does not wire a graph canvas or
AppHost preference callbacks yet. It does not change graph relationships, forces or browsing UX.

| Original source | Windows boundary |
| --- | --- |
| `Cotabby/Models/Vocabulary/VocabularyGraphModels.swift` synthesized settings Codable | Core `Graph/VocabularyGraphSettingsCodec.cs` |
| `Cotabby/UI/Vocabulary/VocabularyGraphPresentation.swift` complete class | App `Vocabulary/VocabularyGraphPresentation.cs` |
| UserDefaults graph Data key `vocabularyGraphSettings.v1` | `AppSettings.Vocabulary.cs` plus `VocabularyGraphPreferences.cs` |
| Notebook `vocabularyBrowserMode` string key | The same settings/preference adapter; canvas/notebook wiring remains next |

## Responsibilities and data flow

One notebook window will retain one presentation and its existing layout worker. The presentation
owns only expanded headword IDs, focused headword, explicit command identity, camera, initial-fit
flags and a wrapping UInt64 epoch. C# `long` preserves the source's 64-bit command serial. UI
mutators reject calls from another thread, adapting MainActor confinement. Frozen expansion sets
and immutable camera/settings values avoid exposing a mutable collection to an older subscriber.

Settings load from copied preference bytes, decode with the complete required-field contract,
normalize once for the presentation, and fall back to original defaults if malformed. Initial
normalization does not rewrite the saved payload. Setting a new value normalizes then saves it;
expansion, focus, commands, epochs and camera are not saved. A new presentation restores settings
and starts with its own worker and fresh transient state. Collapsing an unrelated entry does not
emit a focus change. Layout frames belong to the canvas, not published presentation properties.

The codec reuses the previously verified Foundation-compatible UTF/BOM, first duplicate-key and
strict required-field boundary. All seven settings fields are required despite defaults; labels
are exact `automatic`, `all`, `focused` strings. Unknown labels, missing/null fields, wrong types,
malformed input and overflowing numbers fail decoding. Numeric clamping remains a separate settings
normalization step. .NET can construct an undefined numeric enum that Swift cannot; normalization
closes this additional language boundary to Automatic before encoding or UI observation.

Windows stores the original two keys in its existing `%APPDATA%/Cotabby/settings.json`, rather than
claiming physical UserDefaults compatibility. Graph Data uses a base64 JSON string. Raw JsonElement
properties isolate a malformed key's type/base64 from unrelated app preferences; getters return the
source's absent/default state. An unknown browser-mode string remains a string and selects graph
only when equal to `graph`, as in the Mac view. The adapter follows AppHost's current settings
instance through a function so Reset cannot strand the window on an old object. The future save
callback saves existing settings without applying autocomplete/runtime changes. Preference IO is
best effort, retaining in-memory choices on IO/permission failure, matching the source's lack of
preference error UI. No model, archive or network is involved.

## Evidence and limits

The 34-case Swift verifier executes the complete original presentation and GraphModels Codable
with a private UserDefaults suite for each presentation case and removes that domain with defer
even when observation throws.
It builds with the original worker/GraphSimulation/unchanged Grape package; no presentation body is
rewritten. Twenty codec cases check decoded/raw and normalized payloads, including strict enum/
field failures, duplicate precedence, BOM and UTF-16BE. Fourteen window transcripts check complete
state and saved semantic JSON, initialization versus mutation, commands/epochs, and restart.
The 34 cases do not execute the notebook browser-mode switch; that raw-key adapter is covered
by direct Windows settings tests, and the actual switch remains the next UI slice. Encoded JSON
key order is not claimed stable.

Core has the twenty codec cases and one additional undefined-enum guard. Portable App tests have
the fourteen transcripts and four additional adapter/ownership/notification/thread cases. The
owner-thread test uses a bounded dedicated background thread, not a timing-dependent thread-pool
identity. Tests serialize/reparse the actual SettingsStore representation without touching the
user's settings folder. Initial targeted results: Core21 and App18 passed, no failures/skips.

AppHost injection, the list/graph switch, canvas/drawing/accessibility, visible layout/pointer input
and physical Windows behavior remain unimplemented in this slice. Ordinary compilation or these
portable state checks cannot establish that desktop behavior.
