# Website and online dictionary sources

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
committed files under `Lingrove/`:

- `Services/LanguageLearning/LearningDictionaryRouter.swift` and `OnlineDictionaryClient.swift`;
- `Support/LanguageLearning/DictionaryWebsitePolicy.swift`;
- `Models/LanguageLearning/DictionaryLanguagePair.swift` and `LearningDictionarySettings.swift`;
- `UI/Settings/Panes/DictionarySettingsSection.swift` and `DictionaryWebsiteEditor.swift`;
- `UI/InlineFeatures/LearningDictionaryContentView.swift`.

The shared app dictionary feature owns one router and online HTTP client; the writing and notebook
sessions borrow it. Website settings retain IDs, language metadata, order and enablement. The eight
source recommendations retain their Mac IDs and start disabled when added. Custom templates must
contain one `{query}`, use HTTPS, and cannot put the placeholder or credentials in the authority.
The selected text is percent-encoded as an opaque component. Selecting a website creates a visible
full destination and an explicit browser action; it never loads the site inside the private MDict
WebView. Removing a source changes settings only.

The online English provider targets `https://api.dictionaryapi.dev`, sends only the selected lookup
term, and does not fetch audio. Requests disable cookies, redirects and cache; response reading is
cancellable and bounded to one million bytes, with ten-second header and fifteen-second total
deadlines. The decoder retains source/attribution, license, phonetics, definitions and examples.
It rejects null required fields throughout the complete decoded graph before taking display
prefixes, and uses the existing canonical scalar comparer for Swift string ordering. Errors are
presented without request details. Apple system dictionaries remain unavailable.

Windows privacy adaptation: enabling the online source does not add it to automatic fallback.
Selecting it shows its destination and the data sent; a separate **Look up online** action admits
that request. A changed source, target, or settings revokes the displayed offer. This deliberately
differs from Mac 3b's enabled-source automatic fallback. Local bundled lookup remains the default.
Failed online requests retain their destination disclosure and require an explicit current Retry
action; stale retry buttons cannot admit another request.

The effect boundary is `Inference/LanguageLearning/OnlineDictionaryClient.cs`; portable URL/decode
rules live in Core. `App/Dictionary/DictionaryFeature.cs` owns the one client, settings and router.
Shutdown closes HTTP admission, cancels and joins accepted work before disposing its pool, even if
a retired settings window or local book service fails. A displayed offer alone allocates no request
lifetime. Floating cards and both notebook adapters borrow their existing session and validate the
displayed request and owner before a lookup or external-link action.

The WPF source settings use the shared light/dark palette, Segoe UI, wrapping source cards and
keyboard-accessible reorder controls at a 480-DIP minimum width. The isolated website draft keeps
stable identity, ordering and enablement on Save, validates before enabling the default button,
and supports Cancel/Escape without publication. Website navigation stays in the system browser;
the local MDict WebView and its profile ownership remain separate.

Tests use synthetic data and an injected HTTP handler; no live endpoint, user dictionary or browser
is contacted. Swift/native parity and physical browser, accessibility, DPI, and network acceptance
are separate. The MDict isolated WebView, two-slot owner and profile cleanup are unchanged.

Primary-only implementation/source review follows the maintainer's 2026-10-30 instruction to stop
delegation. This is not an independent agent review. Targeted checks include 95 portable dictionary
cases, five fake-HTTP transport cases and the compiled WPF dictionary/settings suite. Rendered the
real online-offer card and website form/source list in light and dark at 96-DPI output; inspected
the normal and 480-DIP source list for hierarchy and wrapping. No matching Mac screenshots were
available, so IA comparison is source-based. Live HTTPS, physical browser launching, high-DPI and
screen-reader/keyboard desktop acceptance remain for the maintainer's final unified acceptance.
