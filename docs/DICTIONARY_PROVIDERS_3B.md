# Dictionary sources and local MDict

Reference: macOS commit `3b53402f2cbc196b0398d133177fd80006d165b2`, especially
`Services/LanguageLearning/MDictLibraryService.swift`, `LearningDictionaryRouter.swift`,
`LearningDictionarySession.swift`, `Models/LanguageLearning/LearningDictionarySettings.swift`,
`LearningDictionarySources.swift`, `DictionaryLanguagePair.swift`, `Support/LanguageLearning/DictionaryContentPolicy.swift`,
`DictionaryHTMLPolicy.swift`, `DictionaryLinkPolicy.swift`, `UI/Settings/Panes/DictionarySettingsSection.swift`,
`UI/InlineFeatures/DictionaryHTMLView.swift`, and `Packages/MDictReader`. Source-derived cases
are not presented as execution of Swift on this Windows host.

`DictionarySettings` holds the current source catalog, priority, enabled state, definition language,
and local book descriptors. The explicitly selected MDX and matching MDD files remain at their
original paths. Removal only removes the catalog entry. The shared serialized Win32 service validates
and opens them, bounds blocks, decompression, aliases and resources, and retires cached readers when
the descriptor or files change. Existing sessions borrow the same router; source changes retire stale
work. Unknown entry language is retained, not guessed. The original Apple system dictionary and online
providers are unavailable here; selecting one reports an unavailable source. No network dictionary
provider is activated. Loose directory resources and automatic adjacent stylesheets are unavailable:
only resources in explicitly authorized MDD files are opened, preventing a reparse point from initiating
an unvalidated remote file access.

The App-hosted rich article uses WebView2 with a separate temporary in-private profile, JavaScript
disabled, CSP, denied navigation and a request filter that starts with a deny response. Only a current
book's authorized local image, stylesheet and font requests may be fulfilled. It limits each resource
to 8 MB and each presentation to 128 requests and 32 MB. Entry links request a fresh session target;
other navigation, frames, new windows, downloads and permissions are denied. The renderer does not
map a local folder into the browser. Runtime absence is presented as unavailable with installation
instructions; the application does not install it. WebView2 runtime internals and its background
activity are outside this adapter's request interception; independent security review and physical
desktop verification remain required. A controlled native WebView2 component test loads a synthetic
article and a synthetic authorized image in a dedicated temporary profile; it checks that scripting
and web messages are disabled. It does not establish the full browser security boundary or physical
desktop acceptance. Environment and controller initialization have bounded managed waits. On retirement
the control is disposed and a separately rooted cleanup owner waits for the outstanding SDK operation
before attempting private-profile removal; a native operation that never finishes retains that cleanup
owner and profile. Removal retries report failure through the cleanup task. SDK process lifetime and
this deferred cleanup remain subject to independent review. At most two article owners may be admitted;
while both slots remain occupied the article shows a retry action without displaying source text. A slot
is released only after successful private-profile cleanup. A cleanup failure remains internally observed
and retains its slot; it is not reported in the UI. Permanently pending SDK work does not fault and keeps
its slot and profile. This bound is a Windows adapter constraint, not a Mac product rule.

On the Windows development host, the focused Core suite passed 31 tests, including adversarial HTML
and cancellation. The serialized WPF wrapper passed 28 focused tests, including synthetic MDX/MDD
import, local reparse refusal, cancellation and drain, settings, existing cards, WebView2 resource
ownership and delayed environment/controller retirement. The held initialization tests inject barriers
before SDK calls; they do not reproduce an SDK call that itself hangs. Injected cleanup effects cover
denied access, I/O failure, path replacement and confirmed absence. These are component checks, not
user acceptance.

At the 3b reference, `LanguageLearningSettings.resetToDefaults()` calls `LearningDictionarySettings.reset()`,
which publishes an empty catalog and default source preferences. The application-wide Windows reset
must clear `learningDictionaryCatalog.3b`; this removes references only, not the original local files.

The application composition must construct one `DictionaryFeature` with the existing bundled
provider and ICU lowercase/word segmentation and pass its router to writing, reading and notebook
sessions; it must call `ApplySettings` on settings changes and join `DisposeAsync` during shutdown.
The settings pane receives `ShowSettings` as its callback. This module does not itself modify AppHost.
The window uses WPF conventions rather than pixel matching. Broader runtime rendering, screen-reader,
keyboard, real dictionary compatibility and physical desktop acceptance remain unverified. Tests use
synthetic local fixtures, do not read user dictionaries, do not contact providers and do not download
models. See `THIRD_PARTY_LICENSES.md` for the adapted parser and package attribution.
