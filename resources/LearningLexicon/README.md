# Pinned learning expression data

The three `learning-expressions*` files are byte-for-byte copies from Mac Lingrove commit
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, `Cotabby/Resources/`. Keep the JSON together with its
[NOTICE](learning-expressions.NOTICE.md) and [provenance](learning-expressions.provenance.json).
The data is attributed to Wiktionary contributors and Wiktextract/Kaikki and distributed under
CC BY-SA 4.0, separately from application source. Definitions are English; language coverage varies.

The pinned data version is `kaikki-2026-10-03-v1`, 34,397 entries, 11,543,088 bytes, SHA-256
`ff66f9835d9cf8f586d0618c09ad3e2b091269bc15bcdac1502660dd0e4c7984`.
Do not regenerate or update it as part of routine Windows adaptation. The lexical Swift verifier
compares each file with the pinned Git bytes and round-trips all entries through Mac's Codable model.
See [the Windows service mapping](../../docs/LEARNING_OFFLINE_LEXICON.md) for implementation and gaps.
