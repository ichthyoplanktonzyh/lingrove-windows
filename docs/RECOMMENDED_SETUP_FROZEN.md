# Recommended first-use setup

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`:
`Support/Onboarding/RecommendedSetupPolicy.swift`, `OnboardingTemplateRecommender.swift`,
`Models/Onboarding/OnboardingTemplate.swift`, `App/Coordinators/RecommendedSetupController.swift`,
`WelcomeCoordinator.swift`, `UI/Onboarding/Welcome/RecommendedSetupView.swift`, `WelcomeView.swift`,
and `LingroveTests/Support/Onboarding/RecommendedSetupPolicyTests.swift` and
`LingroveTests/App/Coordinators/RecommendedSetupControllerTests.swift`.

`Core/Models/RecommendedSetupPolicy.cs` chooses the frozen quick completion model below 16 GiB,
otherwise the everyday completion model, plus the pinned small translation and explanation models.
`Win32/Models/WindowsPhysicalMemory.cs` obtains physical RAM; an unavailable measurement chooses
the smaller tier. `App/Hosting/RecommendedSetupController.cs` is owned once by AppHost, keeps the
operation after its WPF view closes, and borrows existing download, assignment and source owners.
It does not create or borrow a task's live inference runtime. Constructing or navigating to the
card is inert. Apply and Retry dispatch transfers explicitly; Cancel stops the owned transfers and
normal shutdown drains them. Status is scoped to its model identity, and progress updates from a
retired operation cannot overwrite the current status.

The existing settings file retains `RecommendedSetupApplied`, `RecommendedCompletionTarget` and
`RecommendedCompletionPrevious`. The pending receipt survives restart and admits the installed
choice only while the previous completion selection, local engine and power-switching state still
permit it. Manual configuration clears the receipt but leaves transfers running. Translation and
explanation choices are filled only when empty; selected translation endpoints remain untouched and
the destination is disclosed. Languages, shortcuts, endpoint credentials and other context settings
are preserved. Apply enables only the currently implemented manual sentence-translation feature;
Retry does not re-enable preferences the user later disabled.

The recommendation card uses native Windows controls, displays task/model/status hierarchy and an
upper-bound estimate (the exact remaining-byte calculation is not yet ported), and explicitly marks
speech and automatic expression assistance unavailable. Mac's Apple Intelligence and system-speech
providers are not activated or simulated. Windows does not yet have learning power profiles or the
Mac selected-text/automatic learning workflows. The persisted completion selection is committed
without loading weights, as on Mac. An eligible local writing request can then join one cached-model
preparation per selection epoch; individual requests cancel their waits without cancelling shared
preparation. The admission owner retires stale selections and drains native work at shutdown. Field
and caret freshness are checked again before generation; endpoint requests do not invoke local
readiness. Input never starts a download. The coordinator requires a positively clear composition
receipt with a stable sequence before preparation, generation, presentation and acceptance; a
composition notification withdraws work and prevents the cached accept key from being consumed.
The separate Windows evidence producer and learning adapter must be integrated and independently
reviewed before production acceptance, and native preedit behavior requires physical verification.
Do not present selection, download or compile as native model readiness.

The 500 × 500 offscreen WPF render was inspected for task hierarchy, actions and unavailable state;
no matching Mac screenshot was available. Native focus, keyboard, localization, high contrast,
restart during transfer, network transfers and physical runtime/model behavior remain unverified.
Tests use fake transfers and synthetic state only; no model weights are downloaded.

Windows SDK 10.0.402 validation: `dotnet build Cotabby.sln --configuration Release` passed with
zero warnings/errors. The focused Core filter `RecommendedSetupPolicyTests|LazyCompletionReadinessTests|CompletionModelAdmissionTests`
passed 22/22; `ModelAssignmentSettingsTests` passed 5/5; `RecommendedSetupTests` passed 6/6 on the
WPF dispatcher. These tests do not establish native IME or physical desktop behavior.
After merging the composition checkpoint, the full Core and portable App suites passed 4,752/4,752
and 1,228/1,228. The focused Inference composition/model/insertion filter passed 36/36 and the WPF
setup/composition filter passed 8/8. These checks do not establish physical IME or native model behavior.
