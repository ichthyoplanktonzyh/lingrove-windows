# Canonical CLI candidate

Reference: `3b53402f2cbc196b0398d133177fd80006d165b2`, read from the committed macOS tree.
Source: `CLI.md`, `LingroveCLI/LingroveCLI.swift`, `LingroveCLI/CLIInputSchema.swift`,
`Lingrove/Models/CLI/{CLIProtocol,CLICaptureModels,CLICaptureContract,CLIResourceContract}.swift`,
`Lingrove/Support/CLI/{CLIArguments,CLIInputPolicy,CLIFileIO}.swift`,
`Lingrove/Services/CLI/{CLIMailboxServer,CLIInstallation}.swift`,
`Lingrove/Support/Vocabulary/{VocabularyCLIPlan,VocabularyCLICapturePlan,VocabularyCLIResources,VocabularyCLIClassificationResources,VocabularyCLIReadBudget}.swift`,
`Lingrove/Services/Vocabulary/{VocabularyArchive+CLI,VocabularyArchive+CLIResources,VocabularyStore}.swift`,
`LingroveTests/Services/CLI/VocabularyCLITests.swift`, and
`LingroveTests/Support/Vocabulary/VocabularyCLICaptureTests.swift`.

This is an unactivated candidate. `Cotabby.Cli` parses commands, reads only explicitly named,
bounded local inputs, attaches image bytes and exchanges bounded JSON through a private mailbox.
The app-owned `CanonicalCliMailbox` borrows `ICanonicalVocabularyCliStore`; its constructor does
no IO. Composition must explicitly start the server and drain it before retiring the sole Store.
The Store serializes requests with all existing notebook work. Graph validation, staged image
writes, SQLite revision and idempotency receipt share that existing transaction boundary; replay
checks the receipt before decoding images. No AI or Anki service is called by this module.

On Windows the mailbox is `%LOCALAPPDATA%\Cotabby\Automation-v1\<identity>`, where identity is a
hash of the selected executable's final path using case-insensitive Windows path handling. This
separates routing for different application paths, **not** notebooks or product channels: all
current instances use the existing single Cotabby data root and app single-instance mutex. The
selected executable and its resolved parent directories are held and rechecked for launch. This
is a path/identity guard, not a binary publisher trust guarantee. A competing app instance can
make the selected path unavailable or cause a timeout; the CLI never falls back to its mailbox.
A CLI start is background-only and suppresses duplicate-instance and first-use dialogs without
changing the saved first-use setting. Replies and pending requests expire; SQLite receipts do not.

General settings offers explicit user-local installation of `lingrove.cmd` under
`%LOCALAPPDATA%\Cotabby\bin`; no PATH modification occurs. Its ASCII script passes the UTF-8
app path as base64 to a small apphost in a content-addressed private launcher directory. The
launcher pins the real app and CLI executable identities and forwards the native arguments,
standard streams and exit code. The script disables delayed expansion and never overwrites a
different file. The script's app default can be overridden by `--app`.

The shared 3b native corpus observes typed nil/UUIDs, literal patch nulls, integer-valued floating
JSON, Unicode/slash encoding and several UTF-16 searches. The Windows implementation was corrected
for the source-observed `1.0` integer encoding and canonically equivalent character range.
Other floating-point representations and Unicode substring cases are not established by these
bounded probes. Source-generated identifier collisions
currently fail closed instead of adopting or overwriting an existing record. Windows' existing
image preparer admits its documented PNG/decoder subset; native visual/desktop acceptance and
actual app-owned startup/shutdown integration remain open. Existing historical fixture pins and
old Windows/macOS data are untouched.

The bounded native check is `python3 scripts/verify-cli-3b-macos-parity.py --mac-repo <checkout>`.
It reads `CLIProtocol.swift`, `CLICaptureModels.swift` and `VocabularyCLIPlan.swift` at 3b, keeps
the actual Codable declarations, fingerprint and operation ID bodies, and extracts the NSString
range calculation. A tiny NSRange wrapper supplies only the unused model container. The shared
`fixtures/parity/cli-3b-source-v1.json` records observations from macOS CI run 40944180841.
The two mismatches observed there were fixed in Windows against the source results; a successful
post-fix native rerun is still required.

Executed in the isolated development tree: CLI Core tests, named SQLite Store dry-run/commit/
replay and injected SQL rollback, private mailbox event/lease/recovery/client exchange, image
SQL rollback and retry through the WPF test lease, app/CLI Release builds, and app-output CLI
version/schema/error commands. These checks do not establish physical Shell acceptance, native
Swift behavior, final integration, or a release.
