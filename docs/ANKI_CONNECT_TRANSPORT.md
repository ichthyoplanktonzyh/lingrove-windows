# Local AnkiConnect request boundary

Reference: Mac `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`.

| Original boundary | Windows counterpart |
| --- | --- |
| `Models/Review/ReviewModels.swift` connection snapshot | `Core/Review/AnkiConnection.cs` |
| `Models/Review/AnkiWireModels.swift` connecting protocol | `Core/Review/AnkiConnection.cs` `IAnkiConnecting` |
| `Support/Review/AnkiReviewPolicy.swift` endpoint method | existing `Core/Review/AnkiArchiveEndpointPolicy.cs`, now also normalized output |
| `Services/Review/AnkiConnectClient.swift` framing and private encoder | `Core/Review/AnkiWirePolicy.cs` |
| same complete client and `Services/LanguageLearning/LearningEndpointSession.swift` | `Win32/Review/AnkiConnectClient.cs` |

This supplies an unused, app-ownable service boundary. It adds no connection settings, key storage,
probe button, review window, Store receipt, Anki mutation, media upload or automatic request. A later
review controller must explicitly borrow the service for an operation chosen by the user.

## Ownership and flow

`AnkiConnection` is an ephemeral endpoint/key snapshot with no generated value-style `ToString`.
`IAnkiConnecting` keeps controller tests independent of HTTP, as in Mac. Each production client owns
and disposes one HttpClient/handler; it owns no vocabulary state, inference runtime or transaction
retry. The platform service lives in Win32 alongside other external effects; its actual source is
also compiled into the portable App test project to exercise managed HTTP handling on both runners.

The endpoint rule parses the original authority before .NET Uri can expand abbreviated IPv4.
Only original HTTP localhost/127.0.0.1/loopback-IPv6 forms are admitted; user info, non-root path,
query, fragment and invalid port reject. Normalization retains original host decoding/lowercasing,
root-path removal and numeric-port behavior. It never changes retained notebook strings.

`AnkiWirePolicy` builds action/version6/params and includes a key only when nonempty. Its dedicated
encoder preserves Foundation slash/control escaping, raw Unicode, shortest-number presentation and
UTF-8 byte accounting. A validation/measurement pass visits the whole value before applying the
18 MiB request cap; a late invalid number therefore remains an encoding failure even after large
valid material. Bodies larger than the cap are rejected before allocating/transmitting encoded
bytes. The original client's reused SpeechError oversize wording is retained explicitly.

The transport posts application/json, accepts exactly status200, reads at most2,000,001 bytes and
rejects the byte beyond2,000,000. Status failures never read the body. Every response and stream is
disposed on success, failure or cancellation. No retry is performed. JSON decoding has the original
512-container reader setting; the existing observed 511-container encoder boundary is retained.
A decoded envelope needs both error and result, including a valid null result. A non-null error
returns only the original fixed rejection message; malformed JSON maps to the original fixed
connection message. Server messages, request fields, key and inner transport exceptions are never
retained in application errors or logs.

`Text/FoundationJsonInput.cs` extracts the existing notebook codec's byte-detection method unchanged.
Both consumers now share strict UTF-8/16/32 admission and Foundation's observed BOM precedence,
while retaining separate shape/depth/error rules. The notebook codec's full Core suite still runs.

## Necessary platform adaptations and evidence limits

The default Windows handler disables redirects, cookies, proxy use and default credentials. HTTP
content decoding supports gzip/deflate/Brotli; the response cap applies to the decoded stream.
The loopback-only client does not use the inference endpoint's configurable destinations.
The 10-second .NET timeout covers the header wait and resets before each asynchronous body read,
adapting URLSession’s idle-data timeout so sustained transfers may exceed ten seconds. Native
buffering and timer scheduling are platform-specific; exact wall-clock timing is not claimed. Caller cancellation stays cancellation;
a transport timeout/failure exposes only the fixed connection message. The handler seam is internal
and cannot be selected by a product user.

`anki-connect-v1.json` contains88 source-policy-extension observations. The verifier compiles the
complete original client, original wire values and endpoint/session dependencies. An in-process
URLProtocol supplies synthetic bytes/status/errors and captures outgoing request metadata/body;
no socket, server, collection, model or external network is used. A same-file extension calls the
unchanged private encodedBody method. Original endpoint and SpeechError declarations retain their
full exercised bodies. No copied implementation supplies expected results.

Cases cover strict/normalized endpoints, optional key, typed parameters, source Unicode/slash/
control-byte budgets, floating-point extremes and100 reproducibly generated/edge number values,
exact request/response limits, null/false/typed results, first duplicate envelope keys, missing or
wrong-shaped envelopes, malformed JSON, status failures and UTF input variants. Single-key encoder
cases compare exact raw bytes; multi-key requests compare JSON values and exact byte count because
Swift Dictionary order is unspecified. Snapshot strings are not newline/Unicode normalized.

An attempted509-level private encoder probe on the async Swift actor hit the native worker's stack
limit (SIGBUS). Those four exploratory rows are not fixture evidence and are not claimed as passing.
The separate review-card corpus observes native JSONEncoder depth on the synchronous stack; the
Windows boundary independently tests that retained guard. Deep async-native stack behavior remains
an explicit source/runtime limitation, not an established client-depth parity claim.

Thirteen additional component cases exercise actual Windows handler configuration, no dispatch for
rejected endpoints, bounded reading, response/handler disposal, caller cancellation, owned deadline,
read-error redaction, pre-transmission validation priority and invalid UTF-16/depth admission.
The fake-handler suite does not establish native socket behavior, real redirects/decompression,
Anki add-on interoperability, modal UI, media upload, persisted note identity or recovery. Those
remain later integration/Windows-host gates. Source English error localization remains open. Compression and redirect handler properties are
configuration evidence; native socket behavior remains unverified. The source timeout meaning is
documented by [Apple](https://developer.apple.com/documentation/foundation/urlsessionconfiguration/timeoutintervalforrequest).
Number notation follows the binary64 exact-integer boundary in the [Swift 6.2 formatter](https://github.com/swiftlang/swift/blob/swift-6.2-RELEASE/stdlib/public/runtime/SwiftDtoa.cpp);
fixture observations, including values adjacent to that boundary, remain the executable reference.

Local validation:88 original-source observations pass; Core3517 and portable App267 pass with
zero failures/skips. Full Release solution cross-build passes with zero warnings/errors. Final-head Windows execution and all44 Mac source verifiers are required
before merge. No installer, artifact upload, model download or network test is introduced.
