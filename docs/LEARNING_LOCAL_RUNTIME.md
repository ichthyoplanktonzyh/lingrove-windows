# Local learning runtime adaptation

This slice adapts stable macOS Lingrove at
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`; it extends the existing manual, read-only
[sentence preview](LEARNING_PREVIEW.md). It implements a CPU inference path, not proof of native
Windows model execution. No weights are downloaded by validation and no package is produced.

## Source and test provenance

Mac production paths below are relative to `Cotabby/`; matching tests are under `CotabbyTests/`.
Windows paths are relative to `src/`, and tests mirror the responsibility under `tests/`.

| Pinned Mac source / tests | Windows adaptation |
| --- | --- |
| `Models/LanguageLearning/LearningModelSettings.swift`, `LearningModelSettingsTests.swift` | `Core/LanguageLearning/LearningChatTemplate.cs`, task backend selector and per-model settings in `App/Settings/AppSettings.cs` |
| `LearningGenerationProfile.swift`, `LearningGenerationProfileTests.swift` | `LearningLocalModelConfiguration.cs`, `LearningTranslationStyle.cs`: immutable style, optional normalized sampler changes and seed modes |
| `Models/LanguageLearning/LearningLocalModelConfiguration.swift`, `Support/LanguageLearning/LearningLocalPrompt.swift`, `LearningLocalPromptTests.swift` | `LearningLocalPrompt.cs`: resolved template/family, exact framing, metadata resolution, local context and specialist limits; `Inference/LanguageLearning/Local/LearningGgufHeaderReader.cs`: bounded header reader |
| `Support/LanguageLearning/LearningTranslationBudget.swift`, `Support/Prompting/ModelContextBudget.swift` | `LearningTranslationRequestFactory.cs` uses the actual tokenizer and loaded context for local requests, retaining the endpoint estimate path |
| `Models/LanguageLearning/LearningRuntimeCatalog.swift`, catalog expectations in model-settings tests | `LearningRuntimeCatalog.cs`: exact catalog filenames, pinned URLs, byte sizes, SHA-256 and context recommendations; `Core/Models/ModelCatalog.cs` excludes exact learning catalog filenames from autocomplete |
| `Services/LanguageLearning/LearningLocalRuntime.swift`, `LearningLocalRuntimeIntegrationTests.swift`, `LearningModelResidencyTests.swift` | `Inference/LanguageLearning/Local/LearningLocalRuntime.cs`: separate LLamaSharp weights/context, exclusive operation lease, cancellation and drain; fake-native runtime tests cover ownership and token loop |
| `Services/LanguageLearning/LearningModelController.swift`, `LearningTranslationStyleIntegrationTests.swift` | `LearningLocalTranslationEngine.cs`: frozen configuration, planned request identity, effective style and generation; endpoint planner also suppresses dormant TranslateGemma style |
| `Services/LanguageLearning/LearningModelCollection.swift`, `UI/Settings/Panes/Engine/LearningModelConfigurationView.swift`, `UI/Settings/ModelManagement/DownloadableModelCatalogView.swift` | `App/Learning/LearningModelCatalogManager.cs`, `App/UI/Settings/Panes/LearningLocalModels.cs`: installed models, explicit catalog Get/import, template/context selection and cancellation |

## Ownership and data flow

`AppHost` constructs the long-lived learning runtime and model-file manager independently of the
autocomplete runtime. WPF sends intent through the preview controller. A manual capture freezes the
backend, selected model, style, context and sampler profile. Each local request creates a short-lived
`LearningLocalTranslationEngine`; it obtains an exclusive runtime operation lease, resolves the
actual template and builds an exact token plan. `ILearningTranslationRequestPlanner` is the narrow
Core seam for this preparation and runs away from the UI thread. Cancellation and request identity
are checked again before publication. The existing streamed-output and completed-result policies
then apply to both providers.

The lease covers preparation through generation or a cache hit. Trusted delimiters allow special
tokens; literal source and style text do not. Automatic selection reads a bounded GGUF header before
weights load: 16 MB per string, 32 MB total, 100,000 metadata entries and 1,000,000 array elements;
nested arrays are refused. Hy-MT variants share an architecture, so the embedded chat template is
needed. TranslateGemma identity can survive a filename change. Unsupported architectures or variant ambiguity not resolved by the Mac family-specific fallbacks
require an explicit template. Missing Hunyuan chat-template metadata retains Hy-MT, and a Gemma3
model without a TranslateGemma marker retains Gemma3. A manual-template allocation is reloaded before an
automatic request so absent metadata cannot be reused as an observation.

Budgeting counts the complete framed token plan and reserves full output against the smaller of
configured and allocated context. TranslateGemma retains its 2,048-token framed-input cap. Nothing
shortens a source to make it fit. Saved styles unsupported by a specialist stay durable while the
request renders its supported natural style, matching the Mac controller. The captured options stay
unchanged for stale-result and cache identity checks.

Each generation clears KV state and creates its own sampler. Only end-of-generation makes a
complete result; exhausting the output limit fails and removes the provisional prefix. Incremental
UTF-8 decoding rejects malformed/incomplete bytes and bounds raw output at 48,000 bytes; the shared
translation output policy separately enforces its 12,000-byte result bound. Shutdown closes admission,
cancels work and waits for any synchronous native call to return before freeing context, then weights.
There is no timeout that frees a still-running native allocation.

## Settings and model files

Fresh installs select Local with no model and no prewarm. Legacy endpoint opt-in migrates to Endpoint;
explicitly selecting Local retains dormant endpoint preferences but makes no endpoint request.
Local errors never trigger network fallback. Context and generation profiles are keyed per filename
with Windows case-insensitive lookup after JSON round-trip. Exact catalog defaults remain 8,192 for
Hy-MT2 1.8B and 4,096 for TranslateGemma 4B; other files fall back to 8,192. The local ceiling is 32,768.
Local and endpoint style/context preferences remain independent.

Learning shares the physical GGUF folder with autocomplete to avoid duplicate weights, while owning
separate model selection, transfer state and native memory. Catalog downloads start only after Get;
imports copy an explicitly selected file. Catalog downloads validate expected size, SHA-256 and the GGUF magic header. User imports
validate the extension and magic header, without a catalog checksum or size cap. Both use staging
files, closed file handles before promotion, and no overwrite of existing weights. Cancellation and shutdown
drain transfers and remove staging files. Model-folder scans, hashing and copies run off the WPF thread.
The synthetic-file tests do not establish successful downloads or compatibility of actual model weights.

## Evidence and remaining gaps

`learning-local-v1.json` and `verify-learning-local-macos-parity.py` check 45 cases using actual pinned
Swift policies; the [fixture record](../fixtures/parity/README.md) describes extracted declarations
and stubs. Core tests cover explicit-template budgeting and planner/cancellation/cache identity.
Inference tests drive production facade/token-loop/parser code with fake sessions, synthetic GGUF
headers and in-memory HTTP responses. Settings tests exercise the exact production serialization
options in memory; they do not write the user's settings file.

Native x64 CPU loading, quantization/operator support, model quality/latency, WPF interaction and
concurrent autocomplete behavior remain unverified. GPU offload is not exposed for this learning
slice. Model-specific sampler overrides are stored and honored, but advanced sampler controls,
power-specific model profiles, external-folder discovery, transfer pause/resume, idle unloading and
prompt/KV reuse are deferred Mac adaptations. Loaded learning weights remain resident until a model
allocation change or shutdown. Cache hits still run local planning first and can open the model.

This does not complete the runtime or Windows-host phase. Reading/selection capture, acknowledged
replacement, automatic learning triggers, explanation, dictionaries, vocabulary, Anki and speech
remain migration work. Statistical multi-source recognition and nearby-language tokenization remain
the explicit limitations described in the preview record.
