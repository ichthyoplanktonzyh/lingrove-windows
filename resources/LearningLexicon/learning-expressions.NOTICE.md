# Learning expression data

`learning-expressions.json` is a derived expression subset of English Wiktionary data
extracted by Wiktextract and published by Kaikki. It contains entries for multiple target
languages; the retained definitions are in English. Coverage differs by language.

Attribution: Wiktionary contributors; extraction by Tatu Ylonen and Wiktextract contributors /
Kaikki; subset selection and packaging by Cotabby contributors.

This independent data resource is distributed under Creative Commons Attribution-ShareAlike
4.0 International (CC BY-SA 4.0), not the MIT license of the extraction software. The
applicable license and full legal text are available at:
https://creativecommons.org/licenses/by-sa/4.0/
https://creativecommons.org/licenses/by-sa/4.0/legalcode.en

Entry-level source links point to the corresponding English Wiktionary page and its history.
The extraction is dated 2026-10-03, from the English Wiktionary dump dated 2026-09-02.
`learning-expressions.provenance.json` records source downloads, checksums, transformations,
and counts. The subset preserves headwords, explicit forms, senses, labels and source links.
Citations, usage examples and audio/media were not copied. The derived dataset remains
separate from Cotabby application source code.

Upstream references:
https://kaikki.org/dictionary/
https://github.com/tatuylonen/wiktextract
https://en.wiktionary.org/wiki/Wiktionary:Copyrights

Regenerate from the recorded local JSONL downloads with
`python3 scripts/build_learning_expression_lexicon.py`. A clean checkout can first use
`--restore-pinned` to recover the exact tracked URLs/checksums (changed files fail verification).
`--fetch --version NEW_VERSION --extraction REVIEWED_DATES` discovers current sources.
Updating requires a new version and reviewed extraction/dump dates; resulting checksums must
be reviewed before committing the resource. Reproduction verifies the final resource hash before
replacing its destination, so a changed transformation cannot silently overwrite a pinned version.
