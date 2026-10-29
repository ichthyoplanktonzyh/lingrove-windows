# Windows host acceptance

The maintainer selected **Word → Chrome → Codex** as the first physical host order on
2026-10-29. This record separates host observations from owned-window tests, offscreen rendering,
compilation and CI. An unsupported provider is a product gap even when its refusal is safe.

## Initial availability observation

Host: Windows x64 10.0.19045. The integrated candidate had been built locally but was not launched
for this attempt; no host behavior of that candidate has passed yet.

| Host | Actual observation | Acceptance state |
| --- | --- | --- |
| Word | Returned as installed and launched. Initial screenshot capture reported `FrameArrived timed out: timed out waiting on channel`; refreshed window selection and one activation/capture retry reported `window capture timed out: timed out waiting on channel`. No test text was entered. | Blocked at automation observation; feature behavior untested. |
| Chrome | Not returned by the installed-app inventory; neither standard Program Files Chrome executable was found. Portable/nonstandard locations were not exhaustively searched. | Availability unconfirmed; feature behavior untested. |
| Codex | Returned under the ChatGPT display name. The available computer-use skill prohibits automating that desktop UI, so no draft or message was changed. | Manual input-box acceptance pending; no message submission required. |

No user document, clipboard, model or endpoint was used. The screenshot failure does not establish
a Word defect. No browser was installed and no system setting was changed during the attempt.

## Checks to execute once observation is available

Use a fresh local document/page and fixed nonsensitive material, then record the candidate revision,
actual host version, scale, input method and observed text before/after. Check editable and secure/
unsupported field admission, caret-middle captures, ghost placement, Tab/partial acceptance, exact
insertion and stale-result dismissal after focus/selection changes. Check actual IME preedit with
physical composition; injected text does not establish IME acceptance.

For writing translation/expression assistance, explicitly select the task's local model or visible
endpoint, verify loading/error/dismissal/focus behavior, and inspect the original selection after
acceptance. Readonly selected-text reading stays pending until its production adapter and entry are
integrated. Check 100%, 150% and 200% scale, keyboard navigation and real high contrast separately.
Codex checks use an unsent draft only. Record refusal versus functional success independently.
