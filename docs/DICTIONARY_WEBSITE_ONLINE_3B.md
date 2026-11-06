# Website and online dictionary sources

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`LearningDictionaryRouter.swift`, `OnlineDictionaryClient.swift`, `DictionaryWebsitePolicy.swift`,
`DictionaryLanguagePair.swift`, `LearningDictionarySettings.swift`, `DictionarySettingsSection.swift`,
`DictionaryWebsiteEditor.swift`, and `LearningDictionaryContentView.swift`.

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
Errors are presented without request details. Apple system dictionaries remain unavailable.

Windows privacy adaptation: enabling the online source does not add it to automatic fallback.
Selecting it shows its destination and the data sent; a separate **Look up online** action admits
that request. A changed source, target, or settings revokes the displayed offer. This deliberately
differs from Mac 3b's enabled-source automatic fallback. Local bundled lookup remains the default.

Tests use synthetic data and an injected HTTP handler; no live endpoint, user dictionary or browser
is contacted. Swift/native parity and physical browser, accessibility, DPI, and network acceptance
are separate. The MDict isolated WebView, two-slot owner and profile cleanup are unchanged.
