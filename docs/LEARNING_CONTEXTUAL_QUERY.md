# Contextual lookup session

This Core library slice adapts `Services/LanguageLearning/LearningDictionarySession.swift` and
the three `LearningContextualQueryTests.swift` cases at Mac revision
`43ba08833a4b373b9b82bd4b4fc168ee18c3cd6f`. The
[WPF word/expression adapter](LEARNING_CONTEXTUAL_CARD.md) now supplies worker-built inputs and
borrows the app-owned explanation service. Native interaction/model execution remain unverified.

`LearningDictionarySession.Contextual.cs` owns the target/page sequencing, while the existing
session partial owns bundled dictionary requests and cache. `LearningDictionaryState` carries
independent dictionary content and immutable explanation snapshots. An optional borrowed
`ILearningExplanationGenerating` provides the same app-owned service as sentence explanation;
the dictionary never constructs a model runtime or takes ownership of that service.

With a service, an explicit open, component selection or Back defaults to the contextual page
and starts one explanation when configured. Without a service, the existing dictionary page
queries immediately. With an unconfigured service, contextual remains the default without model
work, and the dictionary remains available on its tab. Settings changes do not generate. Tabs
retain target identity and request dictionary content only on its first visit; returning to
contextual does not restart a stopped, failed or completed explanation.

The App adapter supplies a worker-built `FromUnit` input from the exact completed translation.
The session freezes that base input, and component navigation uses the existing Mac first-range
offset/substring projection. It does not search for the first equal word. A failed component
projection leaves no explanation target, preserving dictionary navigation. A missing base input
uses Mac's unscoped unit fallback: context equals the target, context kind is None and no original
occurrence receipt is claimed. A supplied invalid or foreign input is rejected rather than
silently explained. Dictionary definitions and source attribution never enter this input.

Mac MainActor serializes target changes and callbacks. The Windows owner uses locks for identity
and immutable state, and runs borrowed service callbacks outside those locks. Each changed input
owns a lightweight explanation session and direct cancellation authority. Equal-input component/
Back navigation retains that owner, answer and live request, matching Mac `setTarget(equalInput)`;
opening again always creates a new owner. Replaced owners are revoked under the target lock,
then cancelled/unsubscribed outside it. The app service/runtime keeps its native drain authority.

Even service event subscription happens outside the dictionary lock. Concurrent activation keeps
one session candidate; losing or retired candidates unsubscribe. The first target snapshot is
drained before foreign activation, so a blocked old subscription/saved callback cannot prevent a
new target's notifications. Saved-store reentrancy and old activations cannot replace the newer
owner. Retired model work holds no strong reference to the dictionary/view owner.

Buttons carry target identity and the exact explanation snapshot displayed. The explanation
session checks the expected snapshot atomically when starting or stopping, so a stale Stop cannot
become a refresh. Its optional owner token rejects old admission/results without invoking view
callbacks under the dictionary lock. `Close`/disposal clears context, target, explanation and cache.
Dictionary requests keep their own request identity and cannot relabel an old result after a word
change; changing pages preserves the active dictionary result independently of the explanation.

`LearningContextualQueryTests` adapts contextual default/lazy dictionary, unconfigured behavior
and stale tab navigation from Mac. Windows extensions cover exact repeated occurrences, Back,
same-input retention, uncooperative replies, fallback/foreign inputs, failed component mapping,
blocked/reentrant saved callbacks, blocked event subscription, stale action snapshots and actual
GC of the retired owner/view while model work is pending. Existing dictionary/session tests remain
in place. These are fake-driven state/lifetime tests, separate from the six pure Swift verifiers.

WPF now connects contextual/dictionary controls and captured-editor revalidation. Source routing/
settings, dictionary references, vocabulary ownership/adoption and durable storage remain follow-up
work. No model or native desktop is executed by these tests.
