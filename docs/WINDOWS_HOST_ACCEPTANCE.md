# Windows host acceptance

The maintainer selected **Word → Chrome → Codex** as the first physical host order on
2026-10-29. This record separates host observations from owned-window tests, offscreen rendering,
compilation and CI. An unsupported provider is a product gap even when its refusal is safe.

## Initial availability observation

Host: Windows x64 10.0.19045. The integrated candidate had been built locally but was not launched
for this attempt; no host behavior of that candidate has passed yet.

| Host | Actual observation | Acceptance state |
| --- | --- | --- |
| Word | Returned as installed and launched; executable file version `16.0.20326.20232`. Initial screenshot capture reported `FrameArrived timed out: timed out waiting on channel`; refreshed window selection and one activation/capture retry reported `window capture timed out: timed out waiting on channel`. No test text was entered. | Blocked at automation observation; feature behavior untested. |
| Chrome | Not returned by the installed-app inventory; neither standard Program Files Chrome executable was found. Portable/nonstandard locations were not exhaustively searched. | Availability unconfirmed; feature behavior untested. |
| Codex | Returned under the ChatGPT display name. The available computer-use skill prohibits automating that desktop UI, so no draft or message was changed. | Manual input-box acceptance pending; no message submission required. |

No user document, clipboard, model or endpoint was used. The screenshot failure does not establish
a Word defect. No browser was installed and no system setting was changed during the attempt.
The maintainer has accepted responsibility for physical acceptance. Development continues without
requiring desktop automation to succeed; a runnable candidate and concrete steps will be handed
over for Word, Chrome and Codex when the relevant user flow is connected.

## Checks to execute once observation is available

Use a fresh local document/page and fixed nonsensitive material, then record the candidate revision,
actual host version, scale, input method and observed text before/after. Check editable and secure/
unsupported field admission, caret-middle captures, ghost placement, Tab/partial acceptance, exact
insertion and stale-result dismissal after focus/selection changes. Check actual IME preedit with
physical composition; injected text does not establish IME acceptance.

For writing translation/expression assistance, explicitly select the task's local model or visible
endpoint, verify loading/error/dismissal/focus behavior, and inspect the original selection after
acceptance. Selected-text, pointer and explicit-region reading now have production entries and
their scoped UIA/OCR adapters; execute their native checks rather than treating integration as
pending. Check 100%, 150% and 200% scale, keyboard navigation and real high contrast separately.
Codex checks use an unsent draft only. Record refusal versus functional success independently.

The [current acceptance candidate](WINDOWS_ACCEPTANCE_CANDIDATE_3B.md) consolidates the runnable
development command, latest implementation records, known limitations and this final host order.
