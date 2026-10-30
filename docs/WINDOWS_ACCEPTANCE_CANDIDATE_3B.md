# Windows acceptance candidate at Mac 3b

Behavior/IA reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
Windows keeps coherent WPF/Segoe UI design, launch-time interface language and Lingrove branding;
Cotabby binary, namespace, settings/storage and IPC identifiers remain stable. This is an
unpackaged development candidate, not a release or a claim of complete native Mac parity.

## Runnable development directory

Use the installed SDK selected by global.json and the [developer build workflow](WINDOWS_DEV_BUILD_3B.md).
On this Windows x64 development host, .NET 10.0.403, VS 2022/MSVC 19.44 and Windows SDK 10.0.26100
are available. Python 3.12.14 is installed without a `py` launcher; both environment check and
build accept its explicit executable path. A portable example, with paths replaced for the host:

```powershell
.\scripts\check-windows-dev.ps1 -RequireNative -Json -PythonExecutable C:\Tools\Python312\python.exe
.\scripts\build-windows-dev.ps1 -IncludeLocalSpeech `
  -NativeBuildRoot D:\DevCache\lingrove-speech `
  -PythonExecutable C:\Tools\Python312\python.exe
```

The development executable is
`src/Cotabby.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/Cotabby.exe` under the checkout.
Its existing CLI/image sidecars are built by project references; the optional independent CPU
speech helper and original notices are placed under Helpers only after model-free checks pass.
No model or prebuilt helper is downloaded. No installer, archive, upload, release, startup entry,
provider selection, audio playback or user-data action is performed by these commands.

The host's initial independent full developer command passed 14/14 native framing/platform checks
and copied the checked 5,894,144-byte helper with matching SHA-256. That build's checksum is
recorded in the workflow handoff; rebuilding later code/toolchains need not reproduce its bytes.
Final code/check/build identity is recorded in the corresponding PR and handoff receipt.

## Connected implementation and evidence

Current source connects autocomplete/streaming, writing previews and exact guarded replacement,
expression candidates/actions, selection/pointer/region reading with UIA/OCR adapters, current-format
notebook/meaning/category/graph/media/import/export/backup, independent Anki card/batch preparation,
speech/settings/audio and private CLI composition. Latest module records in
[MAC_LATEST_BASELINE.md](MAC_LATEST_BASELINE.md) and [PARITY.md](PARITY.md) supersede the early
manual-preview and unconnected-library matrices. Those source claims retain their actual scopes.

The named [workflow](WORKFLOW_FAILURE_INTERFACE_3B.md) and [review](REVIEW_FAILURE_INTERFACE_3B.md)
copy inventories are connected. The Windows supplement contains 522 keys in fifteen interface
locales; the original Mac catalogs are retained byte-for-byte. Fixed owned categories/templates
translate before arguments are inserted; unknown diagnostics, user/model/deck/profile/field values
remain literal. Internal synchronization identifiers remain separate from displayed labels.
Independent review found no additional concrete current-path copy key in its final bounded scan;
that is not linguistic/native-quality proof for every locale or an invitation to translate data.

At source `7b8af218f5b91839d379e60b4e0654286460de3e`, Release compilation passed with 0 warnings/errors;
Core 5157/5157 and App 1278/1278 passed. Independent WPF 1243/1243 and noninteractive Inference
751/751 passed, with zero failures/skips. Tests use owned fixtures and substituted endpoints.
Owned Chinese light/dark/simulated-high-contrast error renders were inspected; no physical Mac
screenshot comparison or real mixed-DPI/assistive-technology acceptance is established. Required
final-head CI and merge records remain distinct from these local results.

## Actual residuals, not historical missing modules

| Area | Current limit and observable result |
| --- | --- |
| Automatic language confidence | No production confidence provider is registered. Multiple eligible writing source languages fail closed and request one explicit source; Windows ELS relevance ordering is not substituted for Mac probabilities. Speech keeps its documented fixed-language/card-context fallback. See [the detector audit](LEARNING_LOCAL_LANGUAGE_DETECTION_AUDIT.md). |
| Apple system dictionary | Dictionary.framework is unavailable. Bundled definitions, MDict with admitted adjacent resources, explicit online lookup and website offers are connected; they do not establish the Apple provider's complete coverage. |
| Corrupt existing image repair | [Identity-bound repair](VOCABULARY_OWNED_MEDIA_REPAIR_3B.md) now connects validated backup/save repair on supported local NTFS/TxF and retains it after later SQL failure/cancellation. Unavailable TxF, EFS/network storage or conflicting handles fail closed; no universal filesystem support or power-loss guarantee is claimed. |
| Image/provider subset | Current virtual-file/encoded/DIB/OLE input includes RLE4/RLE8, embedded ICC and opaque CF_BITMAP/TYMED_GDI. Other layouts, linked V5 profiles, other OLE media and full native color/producer coverage remain unsupported or unverified. See [current media](VOCABULARY_CANONICAL_MEDIA_3B.md) and [bitmap input](VOCABULARY_BITMAP_INPUT_3B.md). |
| MDict/platform lifetime | In-root symlinks/cloud placeholders, unsupported redirected/UNC volume mappings and exact Mac companion collation remain limits. A WebView2 initialization/process retirement that cannot be joined may retain its private profile/admission slot; restart can be required. See [MDict resources](MDICT_LOCAL_RESOURCES_3B.md) and the [provider checkpoint](WINDOWS_PROVIDER_DICTIONARY_CHECKPOINT_20261030.md). |
| Native acceptance | Actual host input, IME, insertion, screenshots/OCR quality, Anki, configured models/endpoints and audio hardware remain unverified until the maintainer's consolidated checks below. Local/CI component success does not establish them. |

Ordinary native rename has no expected-target-ID field. The repair adapter uses an optional
local filesystem transaction to preserve verified-object atomic publication; no nontransactional
quarantine/publish fallback is used. Microsoft discourages new TxF dependencies because future
availability is uncertain; supported capability and safe refusal remain distinct from universal
Mac filesystem parity. The module record details the tested boundaries and native API constraints.

## Consolidated maintainer acceptance

Follow [WINDOWS_HOST_ACCEPTANCE.md](WINDOWS_HOST_ACCEPTANCE.md) in the agreed **Word → Chrome → Codex**
order with fresh nonsensitive test material and explicitly selected task providers. Record candidate
commit, host version, interface language, input method, scale and exact before/after result.

1. Autocomplete: caret-end/middle, full/partial accept, focus/selection change, IME preedit,
   stale dismissal, exact text and ghost position. Refusal and functional success are separate results.
2. Writing/expression: automatic/manual preview, loading/error/cancel, marker/shortcut invocation,
   Unicode candidate commit and source preservation/recovery. Codex uses an unsent draft.
3. Reading: selection, pointer and explicit region; security/origin/geometry checks, real installed
   OCR languages, content quality and focus/dismissal. Unsupported hosts remain explicit.
4. Notebook/review: create/import/edit/reopen, meanings/categories, images/drop/paste, graph,
   learning-material export and complete backup/restore; real Anki profile/deck/template/conflict,
   review/audio preparation and acknowledged sends.
5. Presentation/providers: Chinese/English and an RTL locale after restart, keyboard/accessibility,
   100/150/200% and mixed-monitor DPI, actual high contrast; local/endpoint disclosure, real models,
   system/local/endpoint speech and audible hardware behavior.

The maintainer owns these physical checks. No acceptance result is inferred from availability,
compilation, fake-controlled tests or offscreen renders, and unimplemented/platform rows above
remain visible after a successful ordinary host check.
