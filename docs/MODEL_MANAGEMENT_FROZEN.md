# Frozen task-scoped model management checkpoint

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. This checkpoint adds a shared
assignment preference and a bounded Windows manager surface; it does not complete all frozen
model-management features. Existing Cotabby settings path, model IDs, filenames and per-task
endpoint profiles stay unchanged. No weights are loaded by opening the settings or manager.

## Sources and Windows mapping

- `Services/ModelManagement/LocalModelTaskAssignments.swift`, `ModelDownloadManager.swift`,
  `Support/Runtime/LocalModelTaskPolicy.swift` and
  `LingroveTests/Support/Runtime/LocalModelTaskPolicyTests.swift`: `AppSettings.ModelAssignments.cs`
  owns the lowercased filename to task-string map and source map under the original keys in the
  existing Windows settings document. Parse normalizes map keys and registers explicit legacy
  completion/current power, translation, explanation and speech selections before task filtering.
  Unknown discovered files are not assigned. Unsupported retained selections stay stored.
  Reset preserves assignment and source data, as these keys are outside the Mac reset key list.
- `Services/LanguageLearning/LearningModelController.swift`, `Support/Runtime/LocalModelTaskPolicy.swift`:
  learning installed picker and local configuration apply per-task candidate admission; explicit
  import records compatible choices, verified catalog downloads retain their source, and explanation
  runtime admission retires work when a model becomes ineligible. Endpoints remain separate.
- `UI/Settings/Components/Structure/SettingsModelManagementSheet.swift`,
  `UI/Settings/ModelManagement/InstalledRuntimeModelRow.swift`, `UnassignedLocalModelsSection.swift`,
  `LocalModelDetailsView.swift`, `DownloadableModelCatalogView.swift`, `ModelsFolderSection.swift`,
  and `LingroveTests/UI/Settings/LocalModelPresentationTests.swift`: `ModelManagementWindow.cs`
  provides a task label, 20-DIP header, divider, 620×480 minimum and 720×640 initial window,
  installed selected and in-use states, explicit custom assignment, details, refresh and folder.
  Windows system brushes, controls and window chrome adapt native platform presentation. Learning
  recommendations reuse the current transfer owner and expose Get/cancel; imported files use the
  existing GGUF validation/staging path. Completion retains the existing inline Download Models
  owner rather than duplicating a transfer or prewarming when the manager opens. Learning transfer
  status belongs to the longer-lived settings control and rebinds if the manager closes and reopens.
- `UI/Settings/Panes/Engine/EngineAndModelPaneView+Power.swift`: the completion power pickers use
  task-admitted models and retain an unavailable/incompatible persisted choice as a disabled label.
  Their discovery runs off the UI thread and rejects older scan completions. The tray's model list
  and click handler also apply candidate admission before changing the selected ID. Listing successes
  and errors are gated by the same per-view scan epoch. Retained filename/source presentation is
  bounded and controls are removed without changing stored identities; known catalog entries use
  the frozen display aliases.

`AppHost` owns completion runtime selection. Its synchronous invalidation/async stale-load guard is
an independent coordinated integration requirement: a filtered UI cannot stop an already loaded
old runtime from serving requests by itself. Do not treat this branch alone as safe completion
runtime admission until that host change is integrated and tested. The speech settings owner still
needs to record subsequent explicit speech selection changes; this checkpoint registers only its
existing saved choice. Source maps describe this Windows settings envelope and do not imply Mac data
compatibility.

## Evidence and limits

The Windows WPF test renders `ModelManagementView` at 720×640 with synthetic selected/unknown
models and triggers its actual assignment button; optional `LINGROVE_MODEL_VIEW_SCREENSHOT` writes
that render to a caller-selected temporary file for visual inspection. It uses no app host, file
picker, live model, endpoint or user data. No comparable Mac screenshot was available. A rendered
component is not a pixel comparison or physical desktop acceptance.

The completion recommended downloads still live in the existing inline list, not inside the
manager; completion import and catalog transfer pause/resume, Hugging Face browser, external model
folders, speech manager and protected deletion remain unimplemented. Delete controls are omitted;
no deletion safety is claimed. Learning transfer uses its existing Get/cancel implementation and
does not pretend to support pause/resume. Native typography/DPI, accessibility, keyboard navigation,
window activation, actual transfers, power switching, and real host runtime behavior need further
verification. Existing completion ModelList still scans synchronously on its existing refresh path.
