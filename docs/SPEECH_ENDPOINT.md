# Independent speech HTTP transport

Reference: macOS `43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`, complete
`Cotabby/Services/Speech/SpeechEndpointClient.swift` (including SpeechHTTPTransfer), and
`CotabbyTests/Services/Speech/SpeechEndpointClientTests.swift`.

`Inference/Speech/SpeechEndpointClient.cs` adapts that transport independently of autocomplete and
translation. The shared audio generator will borrow it; it is not registered in AppHost and exposes
no new application action. Its caller must supply the speech task's explicitly selected endpoint,
model, voice, key and bounded clicked phrase. Recognition context, prompts, sampling, language hints,
clipboard and unrelated task data have no transport fields.

Each call constructs a fresh HttpClient/handler/request/response lifetime. The default Sockets handler
refuses redirects and cookies. It sends POST to the existing endpoint's audio/speech URL with exactly
model/input/voice/response_format=wav JSON fields, application/json, audio/wav/audio/mpeg acceptance
and an optional bearer key. There is no server fallback, cookie jar, disk response cache or transport
logging. Only nonempty keys become headers; malformed header values fail with a fixed speech error,
so the UI cannot expose a framework exception containing the credential.

Status must be200–299. Known response length and streamed audio are limited to32 MiB, including the
exact boundary. Empty responses fail as invalidAudio. The returned bytes remain opaque: MIME labels
are not a codec proof, and playback/export retain their existing decoder/duration admission. Status,
size and empty-body failures use the original fixed messages and never include server error bodies.
Caller cancellation belongs to this one exchange; disposing it cannot cancel the next utterance.

Mac URLRequest's90-second inactivity and URLSession's120-second resource deadlines become separate
linked cancellation scopes. The idle deadline resets around response/stream progress; the resource
deadline spans the whole call. Deadline expiry becomes a fixed TimeoutException, while caller
cancellation remains cancellation. An injected TimeProvider supports deterministic adapter tests;
it does not change defaults or become a user preference. Awaited transport work stays away from UI.

## Compression adaptation

An actual unchanged Swift client against a fixture-only loopback server returns decoded gzip,
zlib-wrapped deflate and raw deflate. It advertises gzip/deflate and rejects audio larger than32 MiB
after decoding. The Windows handler enables those same codecs, so its streamed limit applies to
uncompressed audio. The standard [.NET decompression handler](https://source.dot.net/System.Net.Http/System/Net/Http/SocketsHttpHandler/DecompressionHandler.cs.html)
removes the encoded Content-Length when wrapping content; its normal unknown-length decoded stream
still passes through the32 MiB limit. This preserves observed URLSession semantics for compressed
responses instead of passing compressed bytes to an audio decoder.

The source executable also observes an oversized declared compressed length with a truncated tiny
payload as a transport failure, rather than the uncompressed declared-size error. The real Windows
transport produces the same outcome category. This distinction was measured before implementation
was finalized; expectations were not changed to fit C#. Compound/unsupported content encodings,
external TLS/proxy/server variants and real audio codecs have no coverage claim here.

## Evidence

`fixtures/parity/speech-endpoint-v1.json` has38 original-source observations: six adaptations of three
original test methods and32 extensions. Both runners lock version/pin/reference-locale field,38
unique IDs and their hash, provenance counts, source/test references and original/wire runner labels.
The native driver additionally asserts its en_US runtime locale, original method presence, exact
result count and ID order. Adapted scenarios do not execute the original Swift Testing suite.

The verifier compiles the complete unchanged original client/transfer and required value types.
Six cases use the verbatim original SpeechURLProtocol. Scripted extensions control response status,
length, chunks and failures while recording the actual built request. Five further cases use the
real default URLSession and a fixture-only localhost server, with gzip/deflate/raw-deflate success,
decoded overflow and incomplete compressed response coverage. Python records actual wire headers
and body; only its assigned host/port becomes `$LOOPBACK`. The server cancels/closes/joins its own
listener/thread lifetime. It never contacts a public endpoint or receives user data.

Requests compare URL, method, auth/accept/content-type fields and exact semantic JSON fields;
unordered JSON body bytes are intentionally parsed. General URLProtocol cases also observe the
actual request timeout. Audio compares exact byte count and SHA256. Fixed speech failures compare
category/status/message. Platform-native network exception types/messages become a transport category;
caller cancellation remains separate. Native URLSession cookie/storage/cache/resource configuration
is observed read-only. C# compares shared limits/deadlines but does not fabricate those platform-only
configuration flags as Windows output.

The actual C# client consumes all38 observations, including five cases over its real default Sockets
handler and loopback server. Five additional adapter cases cover virtual idle expiry and progress/
resource ordering (two rows), overlapping request cancellation isolation, malformed-key privacy, and
real cross-call cookie isolation/redirect refusal.
The socket test does not distinguish UseCookies=false from an empty per-call cookie jar; the disabled
flag is established by the explicit production configuration and static review. Every exchange/stream/server is disposed or joined before its
test returns. The initial33 native observations remain unchanged after the five wire cases were
added. Native verification and43 focused portable cases pass. The full portable App1176 suite passes
with no failures/skips, and the Release solution cross-build passes with zero warnings/errors.
Core production is unchanged; both CI jobs still execute the full Core suite. The65th Mac CI source verifier includes
the original client and test paths; both final-head jobs remain required before merge.

## Remaining integration

The audio generator/local helper, native synthesis/rendering/codecs, installed voice inventory,
recognition/collation, model-management effects and AppHost/settings/card/review composition remain
work. No real endpoint, voice recording, audible hardware playback, package, release or model
weights participate. These local transport observations establish the scoped adapter, not complete
speech or whole-product parity, and activate no new application transmission.
