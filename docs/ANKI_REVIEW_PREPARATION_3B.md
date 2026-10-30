# Current review preparation and preserved layouts

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.

## Source and Windows ownership

The committed `Lingrove/Services/Review/AnkiReviewSession.swift`,
`Lingrove/UI/Vocabulary/VocabularyReviewSheet.swift` and
`VocabularyReviewLayoutEditor.swift` distinguish the default review from an independently
identified card. Both accept a saved layout as material; restoring a preserved plan creates a
new plan UUID and applies its deck only when the association has no confirmed note. Only the
independent card presents the layout editor. `LearningReviewSettingsSection.swift` owns the
saved-template list and explicit removal through the same Store.

`CanonicalAnkiReviewSession` already retains those plan/material/model/mapping rules. This
upgrade removes the WPF window's extra rejection of a default/preserved layout. The default
preparation form and independent editor keep their distinct IA. A default layout does not turn
into an independent card or select the flexible-card Anki model. Save, Send, conflict choices,
identity protection, settings admission and accepted-operation drain still use the existing
Feature, session/controller and single canonical Store.

The shared `AppendResolvedPreviewAsync` renders the original resolved front/back values with
native WPF text, formatting and validated image bytes. It is used for independent cards and the
recognition preview of a default layout. A disabled card type stays disabled. A mapped/customized
note or explicit field overrides still show the exact mapped fields instead. The presentation
does not parse remote HTML, run scripts, mutate a selection, recreate stable part IDs or acquire
another media owner. Preview image awaits retain the window's version/closing guards.

## Resource and UI adaptation

Static source/side/preset/format labels now use the committed Mac Vocabulary catalog. Enum values,
selection values and ordering remain unchanged. Template names, card names, notebook examples,
authored text/captions, mapped field names/HTML, endpoint/profile/model values and unknown exception
details remain raw content. Speech preparation and destination templates are localized before
arguments are inserted; an argument containing braces, a former product name or a UI source key
is not interpreted as another template or translated.

Sixteen review-specific Windows supplement keys have English plus all fourteen other supported
interface locales. At this checkpoint the supplement contained 134 keys, with the remaining 118
still partial. The [subsequent language upgrade](INTERFACE_LANGUAGE_COVERAGE_3B.md) completes that
language pack and connects further UI/quantity states. The three upstream catalogs remain
unchanged; all-window internationalization is still independently tracked.

The view retains the notebook's Segoe UI Variable/Segoe UI typography, shared theme, native WPF
chrome, scroll/resize behavior, keyboard/default/cancel commands and launch-time RTL direction.
Windows source buttons and typed content remain separate from the borrowed session. Pixel
reproduction of the Mac HTML renderer is not claimed. Chinese light/dark component renders at
96 DPI were inspected for hierarchy, spacing, readable enabled/disabled controls and retained
authored names; high-contrast simulation was also rendered. These are the app's own visual tree,
not user screenshots or physical DPI, focus or accessibility acceptance.

## Evidence and corrected historical gaps

The final solution Release build passes with zero warnings/errors. Independent full WPF validation
passed 1,179/1,179 (zero failed/skipped, 4m07s). Review then repaired two missing `Card side` keys
and the image-filter caption; the final preparation/window/branding suites passed 22/22 after
those label fixes and the optional render/accessible-name test addition. New tests cover a current-format saved default
layout reopening and sending its exact front/back fields under the original model and identity,
preserved-layout Cancel versus acknowledged Save, retained preserved identities, Chinese Mac
labels with unmodified authored text, and destination formatting in all fifteen interface locales.
The Mac brand-resource byte verifier and `git diff --check` also pass. Final independent review
and commit CI are recorded with the PR.

The earlier [independent-card handoff](ANKI_CANONICAL_CARD_WINDOW.md) predates subsequent wiring:
card-only clipboard paste and existing-file drop already use the borrowed clipboard/media owners;
review audio already has its [production integration](ANKI_CANONICAL_REVIEW_AUDIO.md); saved template
listing/removal already uses `AnkiReviewSettingsSection`'s canonical Store subscription and
acknowledged remove command. Those are not newly implemented by this upgrade. This record corrects
the stale broader gap claims rather than recreating already connected features.

Tests use private named stores and the synthetic Anki server. No user notebook, Anki connection,
clipboard, screen, editor, model or audio device is used. Physical Anki, native Shell/clipboard,
IME/keyboard/accessibility, mixed DPI and visual acceptance remain consolidated maintainer checks.
Unsupported bitmap/drop/media variants keep their separate explicit limits.
