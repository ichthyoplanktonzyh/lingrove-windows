# Frozen local model task policy checkpoint

This bounded library checkpoint references macOS `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`.
It does not activate task-scoped routing or claim the model-management UI is aligned.
Existing settings identities and the local model directory remain unchanged.

## Source and mapping

| Frozen Mac source and tests | Windows mapping |
| --- | --- |
| `Models/Runtime/LocalModelDescriptor.swift`, `Support/Runtime/LocalModelTaskPolicy.swift`, `Models/Runtime/LlamaRuntimeModels.swift`, `LingroveTests/Support/Runtime/LocalModelTaskPolicyTests.swift` | `Core/Models/LocalModelTaskPolicy.cs` and `Core.Tests/LocalModelTaskPolicyTests.cs`: catalog task/resource identity, family admission, explicit assignment and template policy |
| `Services/ModelManagement/LocalModelTaskAssignments.swift` and assignment tests in `LocalModelTaskPolicyTests.swift` | The same Core file adapts the pure map operation. The caller owns serialization and shared lifecycle. Unknown task strings are ignored. |
| `Support/Runtime/ModelUsagePolicy.swift` | `ModelUsagePolicy` projects selected completion, completion-power and learning filenames into a protected set. Callers must still recheck this at deletion. |
| `Services/ModelManagement/ModelDownloadManager.swift` | `LearningModelCatalogManager.GetInstalledModelsForTaskAsync` filters a detached assignment snapshot after its existing bounded-header scan; synthetic-file test covers task isolation. |

Mac stores assignments under `lingroveLocalModelTaskAssignments`, a dictionary of lowercase
filename to sorted arrays of raw task strings (`completion`, `translation`, `explanation`, `speech`).
The source URL map `lingroveLocalModelSources` is a separate lowercase-filename-to-URL dictionary;
this checkpoint does not implement that source map. `TryRecord` modifies only the supplied map;
known catalog names keep their curated task, and the Mac speech decoder record exception does not
make a decoder a selectable candidate. Startup must register existing explicit selections and
completion power choices before enabling filtering, and selections, imports and downloads must record
explicit assignments. The application settings owner must serialize updates and publish fresh snapshots.
The current unfiltered picker and runtime paths do not use this opt-in API yet. In particular,
`ModelCatalog.Available` retains the earlier behavior until that wiring and legacy migration exist.
Deletion protection is also a pure projection, not an active deletion guard.

Family filtering and base-token splitting operate on the existing Windows `GraphemeString`
adapter: each admitted extended grapheme retains its original combining marks, and ASCII family
matches cannot end inside a grapheme. Invalid .NET task enum values fail closed, since Swift's
task enum cannot represent them. .NET's Unicode segmentation, classification and invariant
lowercasing can differ from the frozen Mac's Swift/Foundation versions; the focused decomposed
letter regressions do not establish exhaustive cross-platform Unicode equivalence.

## UI audit and pending work

`UI/Settings/Components/Structure/SettingsModelManagementSheet.swift` defines a task-labelled
Manage models sheet with Done, a 20-point header, divider, and minimum/ideal size 620/720 by
480/640. `UI/Settings/ModelManagement/InstalledRuntimeModelRow.swift` separates selected,
selectable, in-use and deletable states. `UnassignedLocalModelsSection.swift` uses a Custom models
disclosure and explicit Use for this task. `DownloadableModelCatalogView.swift` specifies Get,
pause/resume/cancel, retry and progress with wrapping failures; Add Your Own and Refresh are below
the recommendations. `LocalModelDetailsView.swift` provides expandable filename, quantization,
source and location and distinguishes incompatible from unavailable retained selections.
`ModelEndpointConnectionSection.swift` maintains separate task-specific endpoint, credential and
connection state; `ModelsFolderSection.swift` owns folder and optional LM Studio source controls.
Windows `EnginePane`, `ModelList`, and `LearningLocalModels` currently retain earlier inline designs
and are not visual or interaction parity. No matching Mac screenshot was available for comparison.
No Windows view was rendered or changed in this policy checkpoint.

The catalog labels here are frozen metadata, not a change to existing Cotabby runtime identifiers or
persisted names. Platform differences in case matching and Windows file discovery need integration
review. Application wiring, source provenance, protected deletion, full manager controls, transfer
pause/resume, task-aware settings and runtime admission, endpoint isolation acceptance, rendered
Windows comparisons and physical desktop/model behavior remain open. No model was downloaded or
loaded by this checkpoint.
