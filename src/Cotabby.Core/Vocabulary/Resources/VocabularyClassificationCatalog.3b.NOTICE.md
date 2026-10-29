# Lingrove classification catalog v4

The JSON classification dataset is distributed under Creative Commons Attribution-ShareAlike 4.0
International (CC BY-SA 4.0): https://creativecommons.org/licenses/by-sa/4.0/.
This data license does not replace the application's separate source-code license.

## Source attribution

- Ron Moe / SIL Global, **Semantic Domains v4**, 1,792 domains.
  https://semdom.org/ and https://semdom.org/bibliography/
  Source JSON: https://semdom.org/data/semdom.json
  SIL publishes this data under CC BY-SA 4.0.
- Dawn Archer, Andrew Wilson, Paul Rayson, **Introduction to the USAS Category System** (October 2002).
  https://ucrel.lancs.ac.uk/usas/usas_guide.pdf
  Supplemental references for degree contrasts (A13.3, A13.5, A13.6, A13.7),
  causation/modality (A2.2, A7), avoidance (A1.9), and helping/hindering (S8). No tagger lexicon is included.
- Council of Europe, **Common European Framework of Reference for Languages: Learning, teaching,
  assessment** (2001), section 5.2.3.2, pp. 125–126.
  https://rm.coe.int/1680459f97
  Reference for communicative purpose and microfunction grouping. No assessment scales are imported.

## Adaptations and scope

Lingrove retains its v1 category keys, IDs and bilingual labels and added 59 branches in v2. Catalog v3 adds localized presentation without changing those identities. Chinese
summaries condense the explicitly linked SIL domains rather than reproduce their full descriptions.
`scopeNote` records Lingrove's routing distinctions based on those domains; it is not quoted source
text. Examples include selected SIL example words and short Lingrove-authored illustrations.
Each record links source domains and marks adaptation, combination, or a narrower selection;
these mappings do not claim exact ontology equivalence or an exhaustive language-independent model.
The historical USAS/CEFR references supplement the mapped SIL definitions, not an imported tagset.

Guidance was summarized in Chinese and translated into all 15 supported interface languages in v3.
Localized names, definitions and scope notes share the same source mappings and examples. Original
English/Chinese labels remain for compatibility. Examples retain their own language, chiefly English.
Translations were authored directly by GPT-6 Luna language agents at max reasoning effort, without
external translation services or local inference. Translation quality may benefit from native review.
Examples are metadata, not notebook vocabulary. Guidance does not infer memberships or move existing
user meanings during catalog upgrade. Local overlays and custom categories are preserved.

The full upstream SIL source is not bundled. Per-category URLs identify the released v4 domain
pages. The CC BY-SA license text is bundled as `CC-BY-SA-4.0.txt`.

Source JSON retrieved 2026-10-29; SHA-256: `343bece15b45508f85382f650ba48c93ff58737ae74cdd2b3703ac9eb9d949ad`.

## Catalog v4 expansion

Version 4 retains every v3 category record and adds 23 categories. Physical-process guidance uses
SIL v4 1.2.3 (state changes, including melting, solidification, boiling, evaporation, condensation),
8.3.4/8.3.4.1 (self temperature change versus making something hot/cold), 5.5.4 (burning), and
1.3.2/1.3.2.1 and 6.6.7.1 (liquid flow and leakage). Liquid flow also appears under movement;
these are two paths to one category, not duplicated meaning records.

Enabling/hindering combines SIL cause/condition, ability, help/hinder, prevention and avoidance
fields, with the cited USAS contrasts. Making something possible is an authored combination of
those domains, not an assertion that SIL has a separate identically named domain. Negotiation
uses SIL 4.8.4.4, compromise uses the explicit compromise question in 3.2.5.4, concession uses
4.8.4.5, and conditional acceptance combines 3.3.2.1 and 9.6.2.8. Functional readings also reference
CEFR 5.2.3.2. Scope notes distinguish possibility from actuality, hindrance from prevention, and
mutual compromise from unilateral concession. These distinctions are Lingrove routing guidance
based on the linked sources, not verbatim external definitions.

All 23 additions include all 15 interface locales, using the same GPT-6 Luna/max translation
constraints. No external translation service or runtime inference is used for this catalog.
