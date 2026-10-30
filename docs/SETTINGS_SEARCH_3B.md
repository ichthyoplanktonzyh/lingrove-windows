# Settings search at the 3b reference

This module adapts settings-search behavior and navigation from Mac commit
`3b53402f2cbc196b0398d133177fd80006d165b2`. It does not establish complete interface
localization or physical Windows acceptance.

## Committed reference and ownership

The behavior source is `Lingrove/Support/Settings/SettingsSearchRanker.swift`, SHA-256
`6f026f198d36c343b048db7f51d0818143f7cfa4ab2f4dc8854e7dde6a17123b`, and
`LingroveTests/Support/Settings/SettingsSearchRankerTests.swift`. Navigation and presentation
come from `Lingrove/UI/Settings/SettingsIndex.swift`, `SettingsNavigationModel.swift`,
`SettingsSidebarView.swift`, `Components/Structure/SettingsPaneScaffold.swift` and
`Panes/HomePaneView.swift` at that same revision. Sources were read with `git show`.

`Core/Settings/SettingsSearchRanker` owns immutable search observations and deterministic
ranking. `SettingsSearchNormalization` supplies the Foundation comparison projection while
borrowing full Unicode case folding through a narrow value adapter. The Win32 adapter calls
System32 ICU's synchronous `u_strFoldCase`; it owns no handle, settings or retained query.
`App/UI/Settings/SettingsSearchPresentation` joins authored row metadata to the existing pane
and row identities. `SettingsWindow` and `PaneView` own navigation, reveal and arrival lifetime.
No new settings, focus, model, endpoint or AppHost service owner is constructed.

## Rules and interaction

- Foundation whitespace trimming and grapheme tokenization retain the first eight tokens before
  normalization and require every surviving token to match. The source's exact, prefix,
  reverse-prefix, word-prefix, substring and subsequence tiers are preserved. Title weights are
  100/90/80/60/25; keyword weights are 70/55/50/40/12; group weights are 35/30/25/20;
  summary weights are 30/30/30/18. Group and summary have no subsequence tier. Title cohesion
  adds 15 and exact joined-title equality adds 40. Equal scores retain declaration order.
- Matching uses the frozen interface locale, localized titles, authored English aliases,
  keywords, pane groups and fixed summaries. Runtime model folders, settings paths and log paths
  remain exact display content and are excluded from matching. User query text is never localized.
- Results are focusable WPF Buttons with accessible names/help text. Enter in the search field
  opens the first already-ranked result. Empty/no-result input performs no navigation.
- Opening a result selects the original pane/row, clears the query, centers the row and reveals
  a normally hidden target for inspection. Reveal never selects a backend, enables a disabled
  setting or writes settings. Manual navigation clears inspection state.
- A 2.4-second accent wash/ring identifies arrival; expiry removes the pulse while keeping the
  inspected row visible. Generation and timer identity guards reject stale dispatcher work.
  Navigation, unloading and closing stop pending work. The 450 ms ease-in/out fade follows the
  source duration; WPF honors disabled client animations.

The native fold retains width, kana and Indic distinctions while removing the source's selected
Roman/Greek/Cyrillic diacritics and applying locale-sensitive full case folding. Canonical comparison
keys do not change stored strings. The existing editor-fold map is not reused; only its Unicode
extend-property data is borrowed. Malformed UTF-16 and unavailable native text services fail with
an explicit localized unavailable state rather than silently selecting different matching rules.

## Windows presentation and evidence

The existing WPF typography, themed cards and controls remain the presentation adapter. The
result keeps title, group and summary hierarchy; arrival uses an accent wash and noninteractive
ring. WPF centers via its ScrollViewer rather than SwiftUI's spring. Owned synthetic visual-tree
renders at 96 DPI were inspected in light, dark and simulated high contrast; the search result
and revealed row remained legible. These are component renders, not Mac screenshots or native
mixed-DPI/accessibility acceptance.

`fixtures/parity/settings-search-3b-v1.json` contains 43 scored observations and 23 fold observations,
including all-token admission, eight-token budgeting, stable ties, grapheme boundaries, Turkish,
case expansions, accents, width, kana and non-Latin marks. Core tests execute the real policies
with an isolated full-case primitive; WPF tests execute the actual installed ICU adapter and
keyboard, automation invocation, reveal, disabled-value and arrival lifecycle paths.

Local Release Core regression passed 5145/5145. The focused settings-search/interface suite passed
33/33 after a zero-warning/error Release solution build. Independent full WPF regression passed
1212/1212 with no skips before the final 450 ms fade adjustment; the focused suite was rerun after
that presentation adjustment. Final-head CI is recorded in the PR. No user screen, clipboard, editor, notebook, model,
endpoint or audio device is used by these checks.

`scripts/verify-settings-search-3b-macos-parity.py --verify-source-only` verifies the exact committed
source/hash on Windows; it explicitly does **not** execute Swift. The frozen-vocabulary CI job
compiles the unchanged original ranker and runs all observations using real Foundation under
separate `en_US`, `tr_TR`, `zh_CN` and `ar_SA` processes. Its outcome must be checked before claiming
Mac behavior verification. The existing 68-verifier manifest and historical pins are unchanged.

Remaining internationalization includes other authored settings/auxiliary-window and typed-domain
copy. Physical keyboard/screen-reader, mixed DPI and the final Word/Chrome/Codex acceptance remain
the maintainer's consolidated round. This module does not close those wider gates.
