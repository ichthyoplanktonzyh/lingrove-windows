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
  status belongs to the longer-lived settings control and rebinds if the manager closes and reopens;
  retry immediately replaces any previous failure or cancellation with a starting state.
- `UI/Settings/Panes/Engine/EngineAndModelPaneView+Power.swift`: the completion power pickers use
  task-admitted models and retain an unavailable/incompatible persisted choice as a disabled label.
  Their discovery runs off the UI thread and rejects older scan completions. The tray's model list
  and click handler also apply candidate admission before changing the selected ID. Listing successes
  and errors are gated by the same per-view scan epoch. Retained filename/source presentation is
  bounded and controls are removed without changing stored identities; known catalog entries use
  the frozen display aliases. Power choices use the same exact built-in and Windows case-insensitive
  custom ID comparison as the completion catalog resolver.

`AppHost` owns completion selection and borrows one `CompletionModelAdmission` wrapper around the
existing llama engine. A selection/settings/power change withdraws local admission and synchronously
invalidates the coordinator's pending edit epoch before scheduling work. Model download, native load
and generation share one exclusive span, so a switch cannot dispose native state during tokenization
or decoding. Selection epochs reject obsolete loads and output, including selecting A after A→B;
power notifications cannot reauthorize an older resolution. Cancellation is signalled for retired
work, and shutdown joins accepted work and cancellation callbacks before disposing the existing runtime.
Endpoint routing is independent and uses no local admission or fallback. Tray, Home, settings and
welcome readiness borrow the admitted snapshot rather than the physical runtime's `IsReady` flag.

The frozen `Models/Runtime/RuntimeBootstrapModel.swift`, `Services/Runtime/Llama/LlamaRuntimeManager.swift`,
`Support/Runtime/BundledRuntimeLocator.swift`, `App/Core/LingroveAppEnvironment.swift` and
`LingroveTests/Models/Runtime/RuntimeBootstrapModelTests.swift` define reconciliation, model-switch
invalidation, preferred installed-model order and power profiles. Startup and manager refresh keep
an incompatible or unrecognized stored ID, but reconcile a compatible vanished choice to the first
installed candidate, or clear it when none is installed. Windows IDs remain Cotabby IDs, custom
filenames compare case-insensitively, and custom ordering uses Windows/.NET current-culture collation.
The existing Windows cache adapter checks the GGUF signature before considering a file installed;
the Mac locator's filename-only discovery differs for malformed files.
On an explicit power transition an installed profile becomes the current selected model; missing
profiles retain the current selection, and a later manual selection remains until the next profile,
power or model-discovery event reapplies an available profile. Editing either profile reevaluates the current source: an installed active profile replaces a later manual selection, while an unchanged effective selection keeps in-flight completion work. Windows power profiles currently
choose local models only; the Mac endpoint and Apple Intelligence profile choices remain open.
Settings construction stays
inert; the existing app startup or explicit Get initiates preparation. A manager refresh does not
reconcile while an explicitly accepted preparation is still in progress.

The speech settings owner still needs to record subsequent explicit speech selection changes; this
checkpoint registers only its existing saved choice. Source maps describe this Windows settings
envelope and do not imply Mac data compatibility.

The Windows settings application compares the effective suggestion values and endpoint configuration
before retiring the shared coordinator. Equivalent preferences and local-only model changes leave
an in-flight endpoint request alone; endpoint destination, model or credential changes retire it.
Completion status publications carry both the runtime selection epoch and a per-view operation
sequence. Startup, tray, inline model list and welcome progress/results/failures discard obsolete
callbacks, and endpoint or disabled startup does not publish a local-model warning. Custom filenames
are bounded and stripped of control/format characters for display only; their stored IDs stay exact.
These adaptations follow the frozen bootstrap/runtime ownership above and preserve the existing
Windows coordinator and endpoint owners. Fake-driven coordinator and reordered-failure tests cover
these transitions; they do not establish physical runtime or desktop behavior.
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
The fake admission tests execute epoch, cancellation, exclusive generation/load, A→B→A, endpoint
independence, reconciliation and shutdown sequencing; they do not load a native model or exercise
physical power notifications. A compiled app is not evidence of those native paths.
