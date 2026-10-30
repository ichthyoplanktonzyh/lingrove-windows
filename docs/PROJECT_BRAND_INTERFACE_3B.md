# Project identity and Mac interface-resource integration

Reference: committed Mac `3b53402f2cbc196b0398d133177fd80006d165b2`.
This is the Windows presentation/resource checkpoint, not a declaration of complete internationalization.

## Approved resources and project identity

The product name, Home/About/Welcome, tray, shortcut labels, user-visible diagnostics, process
description and current repository links use Lingrove. About/README retain the original Cotabby
and Windows-fork attribution, repository links and AGPL license. README describes current connected
workflows and remaining gaps rather than the historical manual-preview-only product.

The approved logo and activation master come directly from `assets/branding/` at the Mac reference;
the app logo comes from `LingroveLogo.imageset`, README artwork from `.github/assets/readme/logo.png`,
and icon frames from `AppIcon.appiconset`. `scripts/sync-mac-brand-resources.py` reads committed Git
bytes, never the Mac worktree. The Windows ICO wraps unchanged 16/32/64/128/256 PNG frames; it does
not repaint, resize or regenerate them. `--verify` checks source bytes against the same reference.
Brand images retain left-to-right orientation in an RTL interface. No installer or release is produced.

Namespaces, assemblies, executable/CLI sidecar names, pack assembly qualifiers, mutex, registry,
credential, settings and storage identities retain their existing Cotabby contracts. Display names
and HTTP user-agent metadata are separate from those stable effects; no old files are migrated.

## Interface integration

The byte-preserved Mac `Localizable`, `Learning` and `Vocabulary` catalogs remain the primary source.
`InterfaceText.Get` accepts an explicit source table where context matters, retaining that table's
own missing-translation fallback rather than borrowing a conflicting interpretation. Shared UI keys
can fall back to the other catalogs. A separate `Windows.xcstrings` supplements Windows-only text.

Trusted resource translations still naming the former product are adapted only at the resource
boundary, and matching legacy-brand source keys can serve their new Lingrove UI sentences.
Explicit original-project attribution remains unchanged. Format templates are translated before
arguments are inserted; filenames, model names, exception details and user text are never keys.
Mac printf placeholders are converted by the .NET presentation adapter without changing the catalogs.

Authored static literals in WPF properties and known UI factories now use the lookup across settings,
reading, dictionaries, speech/model controls, review and notebook surfaces. The generic `Ui.Text`
factory still accepts raw content: it does not translate all callers. Reading source/result/destination
and unknown messages remain unchanged. Initial XAML translation happens before dynamic data is
inserted and respects bindings for text/content, window titles, headers, tooltips and accessible names. Automation IDs
are not translated. Pause calendar decisions stay in Core; the presentation formats their dates in
the frozen interface locale without changing OS culture or preferences.

## Validation and explicit remaining work

Final Release solution compilation passed with zero warnings/errors, full WPF 1,127/1,127,
portable App 1,278/1,278 and Inference 751/751. Resource/reading checks cover table context,
trusted brand adaptation, unmodified format arguments, raw reading content, retained bindings
and packaged PNG icon frames. The renamed shortcut's stable key comparison remains the same;
its display expectation now names Lingrove. Review repairs include startup status, XAML headers,
tooltips and the current notebook's shared export/restore windows. Fresh independent review
confirmed those auxiliary fixes and the unchanged Mac catalogs; the brand sync verifier passed.
Remote CI remains a separate final-head gate recorded in the PR.

Component renders use only the app's own visual tree at 96-DPI output, including Chinese and Arabic.
They are not screenshots of user content and do not establish native mixed-DPI/assistive-technology
acceptance. No real model, endpoint, editor, clipboard or taskbar installation is used by these checks.

The Windows supplement initially translated its platform-specific keys in English, Simplified
Chinese and Traditional Chinese. The [review preparation upgrade](ANKI_REVIEW_PREPARATION_3B.md)
now supplies sixteen review keys in all fifteen interface locales and removes the default/preserved
layout refusal; the other 118 supplement keys still need the remaining language translations.
All-window fixed-state/format/plural coverage, further resource-key reuse, RTL/native accessibility
and physical UI acceptance remain in the active alignment goal. Reading visual fallback and other
platform/media gaps retain their own module records. Do not label this checkpoint as complete Mac
parity or full localization.
