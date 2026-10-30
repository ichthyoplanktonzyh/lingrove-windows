# Settings metadata and card-header interface copy

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`, committed
`Lingrove/Resources/{Localizable,Learning,Vocabulary}.xcstrings`, and the Windows settings
adapters already mapped in [settings search](SETTINGS_SEARCH_3B.md) and
[the active workflow interface pass](ACTIVE_WORKFLOW_INTERFACE_3B.md).

## Connected scope

The read-only active-path inventory found 60 missing unique settings title/subtitle keys and
12 missing card headers. Two shared keys were supplied in the preceding workflow pass. This
batch supplies the remaining **70 unique keys** in all 15 interface languages, bringing the
Windows supplement from 382 to 452 entries. All 382 preceding entries and all three copied Mac
catalog files remain unchanged.

The keys cover Home, General, Engine, Writing, Appearance, Shortcuts, Apps, Advanced, Performance,
learning model/power cards, macros and terminal cards. They retain the exact authored source keys,
including concatenated subtitles, so existing `Rows.Label`, `SettingsRow.DisplayTitle/DisplaySubtitle`
and `PaneView.CardView` now resolve their translated values. The existing search ranker uses the same
localized labels while retaining English aliases and excluding content-marked paths. No view factory,
runtime rule, default, setting identifier or owner is changed by this resource batch.

Fourteen finite adapters reuse committed Mac labels/guidance; two reuse existing Windows task
captions derived from Mac resources. Each comment names its table, exact source key and reference.
The curated-model guidance composes the original download and
explicit-selection instructions. The Windows translation-only model/endpoint cards reuse their
existing task captions rather than implying that their owner configures other tasks. Fifty-four
Windows labels/descriptions are authored here, preserving modifier/reserved-key rules, verified-range
replacement, allowed-app capture, explicit CLI/PATH behavior, model/power choices, request-history
limits and privacy, and resource-counter limits. Apple-only claims are not copied into those texts.

## Validation and remaining scope

An exact-key comparison against the complete baseline inventory finds no remaining missing key in
that settings title/subtitle/header set. Catalog checks preserve all preceding entries, all supported
locale units and format signatures. The existing compiled interface suite exercises every supplement
template and preview boundary in every locale; settings-search tests cover localized labels and the
separate English/content authorities. Executed build, focused/full regression and final-head CI
results are recorded in the PR.
The local focused `scripts/test-wpf-dev.ps1 -NoRestore` interface/settings-search run passed
38 tests with zero failures/skips before independent review.

This is a copy/resource change through the existing WPF controls and wrapping styles, with no new
host activation or model/network effects. It does not claim a physical settings-window rendering,
mixed-DPI/RTL or translation-quality acceptance pass. Current Mac screenshot references are not
available for this batch; behavior/IA authority remains committed source. The maintainer's final
Word → Chrome → Codex acceptance stays consolidated.

The completed inventory is finite, not proof that every application string is covered. Remaining
auxiliary accessibility names, onboarding XAML copy and finite domain messages retain their backlog
from the workflow handoff. Unknown OS/provider/remote diagnostics and all user/model/path/query/
notebook values remain literal. Native language confidence and media/dictionary platform limitations
are independent of this resource completion.
