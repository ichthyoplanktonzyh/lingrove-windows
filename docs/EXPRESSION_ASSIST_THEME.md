# Expression assistance Windows presentation

Reference: Mac commit `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/Services/Presentation/ExpressionAssistPanelController.swift`,
`Lingrove/UI/InlineFeatures/ExpressionAssistCandidateRow.swift`, and
`LingroveTests/Services/Presentation/ExpressionAssistPanelTests.swift`.
The Windows input and candidate card retain the existing single WPF popup and its keyboard,
IME, explicit selection and nonactivating recovery notice ownership. The Mac uses separate
input and result panels; this presentation change does not claim pixel or native parity.

`ExpressionAssistWindow` follows the shared Windows palette and applies system colors in high
contrast; it unsubscribes its system preference observer on close. The existing notice follows
the same palette and owns its existing high-contrast overrides. The Segoe UI Variable/Segoe UI
surface uses dynamic card, text and input colors; the selected candidate has a visible outline.
The destination disclosure has a bounded, independently scrollable area and tooltip. The
candidate list also scrolls within a bounded area. An outer scroll area accommodates the anchor
monitor's work area, with physical pixels converted using that monitor's effective DPI; keyboard
navigation scrolls both areas as needed to reveal the selected row. No endpoint,
editor, clipboard or model activity is required to render these controls.

Local Windows checks: `scripts/test-wpf-dev.ps1 -Filter 'FullyQualifiedName~ExpressionAssist' -NoRestore`.
With `LINGROVE_TEST_RENDER_DIR` set, the real WPF controls produce light, dark, simulated
high-contrast, short-height and 150% scaled synthetic images; notice tests also capture their
failure and unavailable states. Synthetic scaling is not a physical high-DPI or screen-reader
acceptance test. No Mac screenshots were available for direct visual comparison. Physical
desktop theme switching, IME and accessibility acceptance remain separate.
