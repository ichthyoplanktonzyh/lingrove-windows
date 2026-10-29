# MDictReader provenance

The seven `MDict*.swift` parser files originate from Easydict, revision
`b4eced749d492d283f451f88e5504413e4533f44`:
https://github.com/tisfeng/Easydict/tree/b4eced749d492d283f451f88e5504413e4533f44/Easydict/Swift/Service/Dictionary/MDict/MDictReader

Copyright © 2026 Kuroda Kayn / izual and Easydict contributors. Original file
notices are retained. This parser is distributed under GNU GPL version 3; see
LICENSE. Dictionary content has its own license and is not included here.

Lingrove modifications: removed Easydict's app-specific disk cache; added bounded
input, metadata, decompression, record and result limits; checked arithmetic and
format validation; exposed a narrow file reader. The containing application is
AGPL-3.0; this component retains its GPL-3.0 license.
