# Vocabulary entry and text-content editing

The notebook's Edit command now opens an owned modal editor for the selected sense. It adapts the
stable Mac editor rather than changing its workflow: a full detached entry is the draft, the opening
acknowledged entry is the fixed baseline, Cancel writes nothing, and Save closes only after Store
acknowledges the archive transaction. Failed saves keep the draft available with a fixed error.

Initial reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. Example/expression annotation
editing is now explicitly upgraded to current stable `cc468b89f5c360cc7210fac4f1a8b52197870bf2`;
see [the current contract and evidence](VOCABULARY_EDITOR_CURRENT.md).

| Original Mac responsibility | Windows mapping |
| --- | --- |
| `UI/Vocabulary/VocabularyEntryEditor.swift`: draft, selected sense, tags, example edits, Save admission | `App/UI/Vocabulary/VocabularyEntryEditorWindow.xaml(.cs)` and `Core/Vocabulary/VocabularyEntryEditingPolicy.cs` |
| `UI/Vocabulary/VocabularyContentEditor.swift`: ordered text blocks, format, move, deletion | The same owned editor, using stable block UUIDs; existing image references/captions remain in the draft |
| `VocabularyNotebookView.entryDetail` passes the unfiltered entry to an editing sheet | Notebook resolves its global stable selection against Store's complete snapshot before opening the modal |
| `VocabularyStore.update`, `VocabularyArchive.update/mergeSenses/mergeExamples` | Existing Store/serialized archive/three-way draft merge, without a separate persistence path |

The editor can change expression, dictionary form, selected sense meaning/notes/tags, text blocks,
existing image captions, and example source/translation/contextual meaning/completeness. Text blocks
retain their IDs while changing style/bold/italic, moving or being deleted. New text blocks receive
new IDs and default paragraph formatting. Add-text is bounded by the source's 64-block limit.
Adding an example gives it a new ID/time and fragment completeness; deleting an example is part of
the draft. None of these operations starts generation, inference, speech or a network request.

Tag parsing uses original Character comma/fullwidth-comma splitting, Foundation whitespace trimming,
canonical deduplication and scalar sort. Save admission retains the source checks: no active save,
no image import, a nonblank expression, a live selected sense and no blank example text. A blank
dictionary form or text block does not itself disable Save; archive validation remains authoritative.

Changing original example text now preserves its annotations and re-anchors only its ranges.
Expression edits re-anchor only the selected sense's examples; dictionary form is read on the next
such edit. Swift-equivalent canonical equality preserves onChange scheduling. The initial v43 reset
behavior is retained only as historical native evidence. Archive commit still prunes obsolete
explanations, and explicit contextual-meaning edits still acquire user authorship through its
existing merge rules. Windows performs range work away from the dispatcher, retains each event's
captured inputs, rejects stale output and waits before admitting Save.

Only the selected sense changes. Untouched `Content == null` remains null, and existing image
blocks are never filtered out of the draft. A content edit merges as one complete field. A stale
editor retains captures/senses added since opening; conflicting edits fail rather than overwriting
them. The filtered list projection is never used as a baseline. Review/alias/other annotation fields
remain governed by the current archive graph.

Save disables form and dismissal while awaiting acknowledgement. The window lifetime token cancels
presentation work only; an admitted Store operation still finishes when application shutdown retires
the editor. The notebook tracks its save task immediately and joins it with other admitted work
before Store disposal. Worker completion and canceled presentation do not require a live dispatcher.

## Evidence and remaining work

The twentieth Swift verifier compiles original pinned source snippets and runs 29 actual editor
observations: tags, historical original-text reset, Save-admission predicates and content movement. The value
wrapper adds only exposure/effect qualifiers and Swift onChange scheduling based on value equality;
it does not execute a full SwiftUI environment or render the source button. Expected JSON comes
only from that native driver; normal verification never rewrites it.

The original seven Windows WPF tests, now updated for current annotation preservation and extended
by range/scope/background-work cases, exercise the actual modal/controls with a real memory SQLite archive:
Cancel/blank-example admission, preservation/re-anchoring plus acknowledged save/pruning, conflict retention, concurrent
captures, content formatting/order and retained image references, pending save/retirement, and Edit
from a tag-filtered browser using the full entry. The gated clock controls scheduling of a real
archive operation. The image case admits existing graph metadata through the internal whole-graph
fixture boundary; it makes no claim about image-byte preparation/storage. Tests do not open notebook
files, start a model, read the clipboard or interact with an external editor. macOS cross-compiles;
Windows CI executes these cases before merge.

The [image UI](VOCABULARY_IMAGE_UI.md) now adds explicit file import, draft previews and saved-image
reading; its separate tests include actual temporary notebook files. [Existing-file drops](VOCABULARY_FILE_DROP.md)
and [image paste](VOCABULARY_IMAGE_PASTE.md), including the [DIB subset](VOCABULARY_CLIPBOARD_DIB.md),
now connect the same import flow. Compressed/profiled bitmap variants, virtual/pixel drops, graph/batch/review/export UI, speech, localization and window
geometry persistence remain open.
[Sense actions](VOCABULARY_ACTIONS.md), [contextual explanation/adoption](VOCABULARY_EXPLANATION_UI.md)
and [dictionary sessions](VOCABULARY_DICTIONARY_UI.md) now use the shared Store/notebook. Native
physical keyboard/pointer, editor activation, UIA/IME, ghost pixels, insertion and GPU gates remain.
No installer, artifact upload or release is produced.
