# Editor text compatibility data

`editor-text-data-v1.json` is an offline observation of every valid Unicode scalar on macOS
26.6.2 (25G83), Core Foundation 5026.6, with the probe locale fixed to `en_US`. It contains
1,944 nonidentity case/diacritic folds, 280 source-only compatibility transforms, compressed
character-property ranges and combining classes. It does not contain text written by a user.
The original Mac editor calls an unlocalized NSString range search; `en_US` describes the capture
harness, not a new localized product rule.

The complete generator is `scripts/capture-editor-foundation-data.swift`, driven by the explicit,
no-overwrite `capture-editor-foundation-data.py`. Private `CFUniChar` symbols are used only by that
offline Darwin tool. Windows embeds the JSON and performs no native Foundation calls, runtime
character-data capture, network access or resource update. The JSON records the generator digest,
OS/framework versions, all-scalar count, source revision and Unicode subset digest. The file SHA256
is `f74ae78a4ec4439e54c7e10776299e7a53aa98820548c8f15db788a7687a07a6`.

`FoundationSubstringMatcher` and `FoundationComposedText` adapt the forward-search/folding-buffer
and composed-range responsibilities of Core Foundation, with managed memory and the recorded data.
The explanatory source is Swift Corelibs Foundation, revision
`883ec6b01e4bd18d8356cc3873acaf3c2feaa050`, `Sources/CoreFoundation/CFString.c` and
`Sources/CoreFoundation/include/CFUniChar.h`:
https://github.com/swiftlang/swift-corelibs-foundation/commit/883ec6b01e4bd18d8356cc3873acaf3c2feaa050

The exact explanatory header is also retained in the source tree as `CFUniChar.reference.h` (Git blob
`22fb684dd79f4a06792d9281ad799a0aa7df2cf5`, SHA256
`907c9eff7ec49f3c74ced5e5d39fcde42a076380926f16731b72159d44e8aacd`).
Its immutable GitHub API source is:
https://api.github.com/repos/swiftlang/swift-corelibs-foundation/git/blobs/22fb684dd79f4a06792d9281ad799a0aa7df2cf5
The reference header is not compiled into Windows.

Copyright (c) 1998–2019 Apple Inc. and the Swift project authors.
Portions Copyright (c) 2014–2019 Apple Inc. and the Swift project authors.
Licensed under Apache License 2.0 with Runtime Library Exception; full text is `LICENSE.swift.txt`.
The observed Darwin implementation can differ from that open-source revision. This is scoped
compatibility evidence for the current Lingrove editor corpus, not a universal Foundation clone.

`DerivedCoreProperties-17.0.0.InCB.txt` retains the exact InCB rows and source header from:
https://www.unicode.org/Public/17.0.0/ucd/DerivedCoreProperties.txt
The full source SHA256 is
`24c7fed1195c482faaefd5c1e7eb821c5ee1fb6de07ecdbaa64b56a99da22c08`;
the included subset SHA256 is
`923827fcc4ed65835220b1f4e3f832e784922d114f49f6027245a705edec5789`.
The same rows are embedded in the JSON. They supply the GB9c conjunct boundary used during raw
matching; they do not replace the Mac editor's separate composed-range expansion.
Copyright © 2025 Unicode, Inc.; Unicode License V3 is reproduced in `LICENSE.unicode.txt`.
