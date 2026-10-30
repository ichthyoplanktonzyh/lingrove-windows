# Windows interface language coverage

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
This upgrades actual presentation paths and the Windows language pack. It does not declare
all-window copy, every settings-search behavior or physical desktop acceptance complete.

## Committed sources and resource reuse

The unchanged `Localizable.xcstrings`, `Learning.xcstrings` and `Vocabulary.xcstrings` remain the
primary resources. Reference views are `Lingrove/UI/Settings/SettingsIndex.swift`,
`Components/Structure/SettingsSearchResultRow.swift`, `SettingsQuickLinkCard.swift`,
`Panes/HomePaneView.swift`, `ModelManagement/ModelDownloadTransferControls.swift`,
`Panes/Engine/LearningModelConfigurationView.swift`, `UI/InlineFeatures/MacroReferenceSheet.swift`
and the current vocabulary notebook/editor/category views. Endpoint state comes from
`Lingrove/Models/Runtime/OpenAICompatibleEndpointModels.swift`.

The Windows supplement now has 190 authored keys with all 15 supported interface locales.
This completes the earlier 118 partial keys and supplies the additional view/status/accessible
copy connected here. Thirty-two finite Windows source-key adapters reuse committed Mac table
values; each records its table/key/reference in the resource comment. Existing Windows
translations keep platform wording. The unit-conversion prose describes Windows' offline
implementation rather than claiming to use Foundation Measurement. Macro input/results are
not translated. No runtime case-folding or fuzzy lookup of arbitrary content is introduced.

## Presentation changes

- Settings search matches and displays the same localized titles/subtitles as its rows, while
  retaining authored pane/row identity and English keyword aliases. The no-result template is
  translated before the query is inserted. Home quick links and macro headings/descriptions
  use authored resource keys; accepted-word counts use the frozen interface culture.
- Endpoint model-discovery states use localized Idle/Connecting and the actual Mac count forms.
  Failed responses and discovered model IDs remain content. Learning/completion model controls
  localize labels, unavailable-state suffixes, explicit import dialogs, transfer progress,
  checksum/cancellation/failure notices and import receipts. File/model/source values stay exact
  under the existing display sanitization. No transfer, model selection or download admission
  behavior changes, and no model is fetched by these checks.
- The current production `FrozenVocabularyWindowController`/`FrozenVocabularyNotebookWindow`
  path localizes filters, density, loading/empty states, selection counts and accessible names.
  Category visibility/search/axis and outline chrome use the Vocabulary table. The canonical
  entry editor localizes authored field/action/style/context labels. User tags, query text,
  category titles, entry/sense text and binding IDs remain untouched. The dictionary disclosure
  explicitly uses Vocabulary's `Dictionary` sense rather than the Learning-table interpretation.
- `InterfaceEnumLabels` gives typed enum controls a display template. It never replaces items,
  selected values, saved enum values or part IDs with translated strings. The editor's generic
  action factory remains raw because it also renders a suggested user tag; only fixed action
  call sites perform lookups.
- Arabic region instructions read right-to-left inside their own Border. The capture Window,
  Canvas, physical point conversion and returned rectangle remain left-to-right coordinates.
  Applying RTL to the whole overlay would change the coordinate adapter and is not used.

Interface locale remains launch-only and separate from generation language, task endpoints,
Windows culture and serialization. Neither `CurrentCulture` nor `CurrentUICulture` is assigned by
production localization. Existing Cotabby runtime, storage, credentials and IPC IDs remain stable.

## Native quantity adapter

`Win32/Localization/WindowsPluralRules.cs` calls the installed System32 `icu.dll` cardinal API:
UTF-8 locale, UTF-16 keyword buffer, C calling convention and `UErrorCode` semantics. Each request
owns a synchronous rules handle and closes it in `finally`, including error-status paths. It
admits only supported locale tags and nonnegative integer counts; the longest keyword fits the
bounded 16-code-unit buffer. Missing functions/library or native errors return no category.
The authoritative signatures/ownership are in the [ICU header and API](https://unicode-org.github.io/icu-docs/apidoc/dev/icu4c/upluralrules_8h.html).

`InterfaceText` reads original `variations.plural` values without editing catalogs, selects the
returned category and formats the authored template before arguments. Missing native grammar
uses the catalog's known English one/other source forms, instead of inventing a category for the
selected language. Missing category forms retain the catalog's normal other fallback. Ordinary
labels retain the earlier source-key/table fallback and trusted-resource brand adaptation.

`Connected · %lld models` is connected to its real endpoint count. `%lld entries stored` is also
recognized by the catalog adapter; it does not establish a Windows typing-history UI, and is
not repurposed for autocomplete totals, GGUF model counts or notebook records.

## Evidence and remaining scope

Final Release solution build: zero warnings/errors. The new 27-case WPF suite passes, including
real installed-ICU categories for English, Russian, Arabic, Chinese/Japanese/Korean, French,
Hindi, Indonesian and Turkish; Russian endpoint forms and explicit English capability fallback;
all 190 keys' locale/format-slot coverage; localized search with original aliases/query arguments;
unchanged thread cultures; real enum-selection rendering; RTL hint versus physical canvas; and
current notebook filters with unmodified tags/query/entry data and unchanged Store revision.
The initial isolated enum-template fixture was corrected to use an owned live ContentPresenter
lifecycle; no production assertion was relaxed. Final independent full-suite/CI counts are
recorded in the PR.

The first full regression also caught an unnecessary English selection-count wording change.
The compact `2 selected`/`1 selected` template was restored against the unchanged batch assertions;
the affected interface/batch suites then passed 51/51. The remaining fixed meaning-navigation
headings/count/breadcrumb were also connected without translating node/entry titles.

The notebook render uses synthetic `Close`/`General` content and the app's own visual tree at
96 DPI. Light/dark/high-contrast simulations are component evidence, not screenshots of another
app or native mixed-DPI/accessibility acceptance. All stores and model/endpoint effects in tests
are isolated/synthetic; no user screen, clipboard, editor, notebook, model or audio device is used.

Remaining internationalization work includes a complete inventory of additional authored settings,
auxiliary-window and typed-domain status/error copy, and their format/RTL/accessibility states.
Unknown OS/provider/remote errors remain raw diagnostic content. The Mac
`Support/Settings/SettingsSearchRanker.swift` adds token/fuzzy/relevance rules beyond this Windows
row search; those deterministic search behaviors are a separate explicit gap. The disconnected
legacy XAML notebook is not the production notebook and its labels do not prove a current-path gap.
Full internationalization and the wider alignment goal remain open. Word, Chrome, Codex and
physical desktop/input/provider acceptance retain the consolidated maintainer gate.
