# Frozen local model import and deletion

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`. The source boundaries are
`Services/ModelManagement/ModelDownloadManager.swift` (import, staging, source metadata and
deletion), `UI/Settings/ModelManagement/DownloadableModelCatalogView.swift`,
`InstalledRuntimeModelRow.swift`, `UnassignedLocalModelsSection.swift`,
`UI/Settings/Panes/Engine/EngineAndModelPaneView.swift` and `LearningModelConfigurationView.swift`
(confirmation), `App/Core/LingroveAppEnvironment.swift` and `Services/Speech/LearningSpeechController.swift`
(protected task references). `LingroveTests/Services/ModelManagement/ModelDownloadManagerTests.swift`
supplies file and post-confirmation protection cases.

Windows offers multi-file GGUF import for completion and learning. A compatible explicit choice is
assigned to the task; copying validates the GGUF signature, streams to a unique sibling stage,
and publishes without replacing a collision. The file service acknowledges each successful import's
source removal before releasing the destination lease, including a partially cancelled import.
Catalog downloads acknowledge their source under the same lease. An inaccessible, malformed or
colliding source is not replaced. Windows requires a signature on discovery, unlike Mac's filename
discovery. Assignments survive deletion, and an explicit missing selection remains represented.

The app's per-destination `ModelFileOperations` lease coordinates completion and learning transfers,
imports, deletion and completion loading; it does not share model sessions. Learning's native
runtime owns a Windows read lease while a model is loaded. The UI confirms deletion, waits for the
file lease, then rechecks current completion and power choices, translation, explanation, speech
model and companion, plus the resident completion model before unlinking the app-owned leaf. The
Win32 adapter pins every directory component against rename, rejects reparse points and removes
the verified regular file through its opened handle. Source metadata clears after unlink; a failure
is reported. External folders and recursive deletion are outside this slice. Actual Shell and
filesystem behavior beyond isolated file tests remains native acceptance work.

The Windows WPF row retains Mac's selected and in-use states, explicit Select and Delete actions,
and confirmation, using Windows controls rather than pixel equality. A 720 × 640 synthetic offscreen
render was inspected with selected, in-use and deletable rows. No matching Mac screenshot was
available. Physical keyboard/focus, screen reader, DPI/high contrast, dialog, cancellation and
real model/runtime behavior remain unverified; tests use small synthetic GGUF headers and do not
download or load weights.

Validation on Windows x64, SDK 10.0.402: `dotnet build Cotabby.sln --configuration Release`
passed with no warnings or errors. The focused `LearningModelCatalogManagerTests|ModelDownloaderTests`
Inference filter passed 21/21; `ModelAssignmentSettingsTests` passed 4/4; and
`ModelManagementViewTests` passed 8/8 with the synthetic render enabled. The latter includes a
temporary regular-file deletion, locked-file refusal and path-traversal rejection; it does not
establish physical Shell interaction or external filesystem-race behavior.
