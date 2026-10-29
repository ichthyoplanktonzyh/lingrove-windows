# Historical Mac stable alignment

This audit records the former target **`cc468b89f5c360cc7210fac4f1a8b52197870bf2`**.
It is 22 commits after the initial Windows migration reference
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. The working Mac application is not modified by this port.
The original reference remains the default for existing versioned fixtures. Each module upgrade
must explicitly name its newer revision, preserve old evidence as history, capture the actual new
source behavior and record the remaining product/platform gaps.

The maintainer has now directed Windows to follow the latest Mac implementation. The active target
and backlog are in [MAC_LATEST_BASELINE.md](MAC_LATEST_BASELINE.md). The table below is historical
evidence; it omits later import, expression-assistance, reading and independent meaning/resource
changes and must not determine the next feature's source revision.

This file records the audited change areas and next implementation work, not a percentage-complete
claim. Many earlier slices supply reusable domain/runtime boundaries; that does not mean their
full native providers or user interface are connected. The [parity matrix](PARITY.md) retains the
historical baseline and the scoped evidence for those slices.

| Work area | Current stable reference and remaining Windows work |
| --- | --- |
| Example and expression editing | `VocabularyPolicy.reconciledRanges`, `VocabularyEntryEditor`: annotation preservation and re-anchoring are implemented in the current slice, with [explicit scope](VOCABULARY_EDITOR_CURRENT.md). Current tag chips, tag suggestions and the rest of the editor's visual changes remain work. |
| Read-aloud speech | Original controller, endpoint transport, local runtime/process boundary and independently pinned helper generation loop have been adapted. The [PCM renderer/device](SPEECH_PCM_DEVICE.md) now supplies compiled WASAPI output and continuous tempo processing, with hardware acceptance open. Installed Windows voices/recognition/collation, system synthesis, encoded playback, local model management, AppHost ownership and card/tray controls/shortcuts still need implementation and integration. Model-backed generation/playback remains a separate acceptance gate. |
| Anki review presentation | Existing card material, transactions, connection/controller, audio preparation and single/batch sessions are reusable. Notebook review actions, layout editing, preparation/batch windows and conflict-choice presentation still need the existing Mac flow adapted. |
| Reading translation and OCR | Audit the current `OCRRuntime`, `OCRWorkerProcess`, `ScreenTextExtractor`, `VisualContextCoordinator`/activity and `LearningReadingSourceReader`, together with OCR lifecycle/isolation/reading integration tests. Existing Windows autocomplete visual context and manual learning preview do not establish this current reading flow. |
| Notebook browsing | [Current sidebar adaptation](VOCABULARY_NOTEBOOK_CURRENT.md) connects persisted compact/detailed density, direct single-sense selection, multi-sense header expansion/selection and external selection reveal. Shared Store/detail navigation and the graph solver remain in use. Windows WPF/physical acceptance, native compact language names, graph HUD and remaining detail presentation deltas are tracked separately. |
| First use and guidance | Adapt `HomeLearningDemo`/showcase, `ProductUsageGuide`, and versioned/resumable permission/readiness rules in `WelcomeCoordinator`/`OnboardingSetupPolicy`. The existing Windows step wizard does not establish these flows. |
| Settings and language | Keep existing functional clipboard/application context controls while adapting current grouping and presentation. The current Mac change includes 224 additional translations; Windows still needs its interface-language resource system and corresponding product copy. Existing settings Ctrl+F is already connected. |
| Runtime metrics and supporting capabilities | Audit current helper process-epoch/PID reuse handling against Windows lifecycle/metrics. The whole-target audit must also keep pre-existing gaps such as typing history, vocabulary CLI, and native host acceptance visible; the 22-commit delta alone is not the entire migration backlog. |

Current priority is the visible notebook and learning/review flows, followed by reading/OCR,
onboarding and localization/presentation changes. Preserve authored data throughout. Speech
provider/integration gaps remain explicit, with further audio expansion deferred while these
visible workflows are adapted. Use the existing Mac subsystem ownership and contracts rather
than inventing replacement products. Keep cancellation, stale-result checks, task-specific privacy
and storage identity intact at every step.

Windows CI can execute managed policies, native components and WPF control workflows. A successful
component build does not establish user-host UIA/IME/caret, insertion/clipboard, ghost pixels,
installed audio devices/voices, GPU/model quality, physical Shell or an actual Anki session. Those
acceptance gates remain explicit. Routine checkpoints do not create downloadable packages,
installers, uploaded binaries, releases, or model downloads.
