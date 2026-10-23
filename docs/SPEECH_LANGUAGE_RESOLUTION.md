# Speech phrase and context language resolution

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Support/Speech/SpeechLanguageResolver.swift` and deterministic cases from
`CotabbyTests/Support/Speech/SpeechLanguageResolverTests.swift`.

`SpeechLanguageResolver` belongs in portable Core. A later speech controller supplies an
unconstrained recognizer on an explicit action; the resolver decides whether there is enough
evidence and which result wins. It examines the first600 extended graphemes of the actual phrase,
then independently bounds same-side context if needed, finally returning the card hint. A confident
unsupported language returns null immediately, without consulting context or relabelling it as
the hint. Provider capability is checked later, so Dutch can resolve even though Qwen lacks it.
The translation detector's constrained candidate API is deliberately not reused here.

Evidence preserves the original scalar/character distinction: Foundation letters count scalars,
including combining marks; word boundaries classify the first scalar of each grapheme with
Swift's separate Character.isLetter predicate. Eight letters plus either a counted scalar above
U+024F or three words are required. Review found that Foundation's set omits some scalars current
Unicode categories call letters, so using .NET L*/M* categories changes the evidence threshold.
`SpeechLanguageEvidence` therefore owns two private frozen range tables measured on the Mac
reference toolchain:750 Foundation ranges and766 Swift word-letter ranges. The native verifier
enumerates every valid scalar and checks both tables against the fixture. This freezes the
observed sets; it does not guarantee identical grapheme segmentation across future SDK/Unicode
versions. Corpus cases cover combining marks, Prepend characters, astral text and emoji clusters.
The sample bound is applied while scanning, without building a full index of unexamined context.
No trimming, script guess, provider filter or request transmission is added.
Only finite probabilities in[0,1] count; the winner needs at least0.75 and a lead of at least0.20.
Floating-point threshold subtraction is preserved. Exceptions from the recognizer propagate.

`verify-speech-language-macos-parity.py` compiles the complete original resolver and complete
language/catalog declarations. It injects raw NLLanguage-score dictionaries through the original
closure and observes both the resolved code and every exact sample requested. Sixteen cases adapt
the original deterministic tests;175 source-derived extensions cover all palette/unsupported
codes, invalid scores, threshold neighbours, context ordering, Unicode evidence and grapheme
bounds. Expectations for all191 cases and both property tables were captured from the original executable, with IDs locked
in both runners. The original real-NaturalLanguage examples and Swift Testing suite are not run.

Core runs the same191 cases plus a recognizer-fault propagation case and two whole-table
consistency cases that also verify binary-search membership for every scalar and gap. All194 focused tests and full Core4303 passed with zero failures/skips. Full Release cross-build
passed with zero warnings/errors. Native verification passed locally and independently for191
traces plus both complete Unicode sets. Both final-head CI jobs remain required before merge.

No Windows recognizer or native language accuracy is established here. Installed voices, catalog
selection/collation, settings, live controller/providers/playback and review audio remain work.
There is no AppHost/UI wiring, file output, model download, network test, installer, release or
uploaded artifact. Recognition context is only a local input and must not be forwarded as speech.
