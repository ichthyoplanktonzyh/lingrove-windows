# Writing-card vocabulary favorites

The completed writing sentence card and its dictionary unit/component cards now borrow AppHost's
one VocabularyStore. They adapt Mac's favorite fish control: save the exact detached encounter,
show pending while the archive works, and paint saved only from the acknowledged Store snapshot.
Clicking a saved fish offers removal of that exact favorite/usage. Saving does not invoke a model.

Reference: `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Mac source responsibility | Windows mapping |
| --- | --- |
| `UI/Components/VocabularyFavoriteButton.swift` | `UI/Vocabulary/VocabularyFavoriteButton.cs`: saved/pending/error, exact remove and optional notebook navigation |
| `Assets.xcassets/FavoriteFish{Outline,Filled}.imageset/*.svg` | Exact original path data in `UI/Vocabulary/VocabularyIcons.xaml`; 552-square viewport, 20-unit translation and original 24-unit round outline |
| `LearningSentenceCard`, `LearningDictionaryCard` borrow shared Store | Existing WPF windows expose a favorite binding with their displayed document/target identity |
| `VocabularyCaptureFactory.preview/unit/component` | Existing Core factory, run on the controller worker; source-app identity stays absent pending a stable Windows adapter |
| Mac panel owns capture and child presentation lifetime | `LearningPreviewController.Vocabulary.cs`, dictionary preparation and existing intent/freshness gates |

The controller verifies its frozen editor capture around asynchronous factory work. It does not
read a new source into the favorite when the user clicks. A dictionary component maps into the
same encountered sentence, retaining identity, timestamp and translation. Windows source-app identity remains nil: a best-effort
process basename cannot stand in for the source bundle/application identifier.
Preparation revision and document/target checks prevent late tokenization or dictionary callbacks
from attaching a favorite to a different reused card. ICU failure leaves capture unavailable.
The existing explanation path remains separate from the save action.

The WPF control owns only a binding lease and Store subscription. A new capture, navigation,
hide or unload retires the lease and closes its menu. Late feedback and detached menu events
cannot alter a new card. An admitted database edit still finishes when its originating card hides;
its UI feedback is ignored. Store pending keys also disable other controls for the same encounter.
Fixed archive errors appear inline and in accessible help, adapting Mac's alert to the existing
nonactivating Windows card. Parent view text is not populated from native exception strings.

The source-equivalent `Store.Show` route resolves stable entry/sense IDs. The Show-in-vocabulary
menu item appears when the notebook composition registers that route. This round connects Save and
exact Remove; the notebook window/route remains the next slice, so the current app omits that menu
item rather than presenting an action with no destination. Explicit meaning adoption UI, speech,
reading/selection/OCR favorites and full localization also remain open.

Six added compiled WPF cases use the real in-memory archive and shared Store: acknowledged Save
and exact Remove while preserving another encounter; a gated pending save and retired binding;
fixed failure without a saved claim; stable navigation and an old menu refusing deletion after a
new card; dictionary target change/hide; component capture/Save/Remove preserving the parent pattern. The async STA fixture keeps the dispatcher pumping while
actual SQLite work completes. Existing nonactivating-style and in-memory render checks remain.
macOS only cross-compiles these views; Windows CI must execute them before merge.

These are routed-event/view tests, not physical mouse, keyboard, popup-focus or external-editor
UIA/IME tests. They do not establish Windows caret/ghost geometry or GPU/model parity. The full
Mac capture/Store/Archive verifiers retain their original values; no model, endpoint, editor,
installer, release or downloadable artifact is involved in this UI slice.
