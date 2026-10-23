# Readonly explanation-selection adapter

The Windows adapter now connects the sentence card's explanation meaning/prose and the contextual
word/expression card's meaning, example, reciprocal line and Details. It uses the app-owned copy
worker and captured-document grants. This is the mouse/context-copy subset; native foreground-host
behavior and the interaction differences listed below remain unverified.

The source responsibility is Mac `Cotabby/UI/InlineFeatures/LearningExplanationView.swift` at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`: meaning, explanation prose, contextual example and
its reciprocal translation enable readonly text selection. Sentence source/translation instead
use nonselectable `LearningSentenceDictionaryText` and explicit Copy buttons; this adapter does
not change that flow. The pinned Mac card tests render nonactivating panels, but do not establish
end-to-end mouse-selection/keyboard-copy behavior in those panels.

WPF's built-in TextEditor mouse selection first requires keyboard focus
([TextEditorMouse.cs, .NET 10](https://raw.githubusercontent.com/dotnet/wpf/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Documents/TextEditorMouse.cs)).
The adapter instead uses TextPointer hit testing without TextEditor, a mouse-transparent Adorner
for selection paint, and a plain nonfocusable Popup with Copy/Select all buttons. Popup's native
window uses no-activation styles
([Popup.cs](https://raw.githubusercontent.com/dotnet/wpf/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/Popup.cs));
using ContextMenu would enter menu keyboard-focus handling
([ContextMenu.cs](https://raw.githubusercontent.com/dotnet/wpf/v10.0.0/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/ContextMenu.cs)).
There is no call to Focus, global Ctrl+C interception, editor read or clipboard write in the control.

| Boundary | Ownership and data flow |
| --- | --- |
| Core `LiteralTextSelectionMap` / `LiteralTextSelection` | A control-owned immutable index validates one bounded literal UTF-16 string and segments its graphemes once. Binary search expands two offsets to whole combining/emoji/CRLF sequences; selection retains exact original text without normalization or trimming. |
| App `NonactivatingSelectableText` | TextBlock adapter owns mouse capture, offsets, visible-line highlight geometry and a nonfocusable popup. It supports literal Runs only; it does not rewrite their styling or parse markup. TextBlock's sealed renderer stays responsible for shaping and layout. |
| `SelectedTextCopyLease` / event args | Copy emits detached selected text, the owner-supplied document identity and an atomic current flag. Changed text/document/selection, hide and unload revoke pending receipts. It contains no WPF TextPointer, field or provider. |

`UpdateLiteralContent` brackets a renderer's synchronous annotation rebuild. Equal raw text keeps
its selection and index; changed text drops both. The index reads the actual literal Runs, not
TextBlock.Text: WPF installs its text-container listeners only at first measure, so initial Runs
can paint while that dependency property still holds an empty value. Content notifications and
wrapped refreshes update the index; dragging does not concatenate/resegment the whole document. Direct Text/Run/inline changes must revoke any
already-issued receipt as well, with dedicated Windows regression cases. Cards must supply an
immutable identity and validate their current capture/target when granting an eventual native copy;
a lease alone is not editor or document authority. Once the native writer has admitted its one-shot
transaction, it completes under the [existing copy contract](LEARNING_COPY.md).

`LearningPreviewWindow.Selection.cs` lends its completed request ID to the embedded explanation.
`LearningDictionaryWindow.Selection.cs` lends its exact target GUID to contextual text. Every
literal refresh/inline rebuild uses the wrapper; unchanged text keeps selection during generation
refresh and lexical updates, while new input/target/page, hidden Details or card retirement revoke
pending receipts. Source/translation and the plain dictionary-page example keep their existing
nonselectable presentation, matching the pinned Mac responsibilities.

`LearningSelectedCopyPolicy` rechecks the exact completed parent document and, for dictionary text,
the original target and contextual page. `LearningPreviewController.SelectionCopy.cs` adds the
existing capture, intent, copy revision, direct lifetime and cached-field checks at final grant.
Dictionary navigation intent also revokes a queued selection copy before its async UIA validation
commits the new page/target; old feedback cannot cross that navigation revision.
It shares the one background worker with the explicit sentence Copy buttons and sends fixed
feedback only to the still-current owner. The worker reads detached data/atomic flags, never a
Dispatcher-bound control. Dictionary feedback does not replace source/model attribution.

The grapheme index is reused across dragging and rendering. Visible-line bounds are intersected
with ancestor ScrollContentPresenter viewports, then binary-searched into that index; geometry
does not scan each offscreen grapheme. Bidi cells are coalesced per visual line to avoid repeatedly
darkening ligatures/overlap. The Adorner is removed when selection retires and never handles input.

Portable cases cover exact/reversed ranges, decomposed Unicode, emoji, CRLF, whitespace/markup,
invalid offsets/source and repeated index queries. Windows WPF cases run actual hit testing,
styled-Run preservation, raw-text/document/hide revocation, old-menu rejection, native popup styles,
wrapped bidi geometry, a scrolled long-document viewport and in-memory before/after pixels proving
that the selection Adorner paints. They attach no clipboard writer and synthesize no global input.

This is the mouse-drag and context-menu subset. Word double-click selection, drag autoscroll,
keyboard/accessibility selection commands and actual foreground-host behavior
remain open. Ctrl+C continues to belong to the active writing host. Native Windows tests must still
check physical pointer/capture, popup dismissal, clipping under mixed DPI and focus retention in a
real editor. No model, network, clipboard mutation, package, uploaded image/artifact or release is
part of this stage.
