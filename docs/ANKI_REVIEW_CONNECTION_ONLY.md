# Anki connection settings without a legacy notebook

Reference: Mac `35b7fbe5c788c5524936ade1dfb7c89a3a285b04`,
`Lingrove/UI/Settings/Panes/LearningReviewSettingsSection.swift`,
`Lingrove/Services/Review/AnkiReviewController.swift` and
`Lingrove/Services/Review/AnkiReviewController+Navigation.swift`. The source grouped form keeps
connection/settings work independent of card-template storage and makes network testing explicit.

`AnkiReviewFeature.CreateConnectionSettings` borrows the caller's real settings getter, persistence,
AnkiConnect client and credential owner without constructing or opening a notebook. Construction
is inert. Visible settings read the key, changed keys save on the existing explicit interaction,
and Test uses the real injected client to probe the connection, profile, decks and fixed note type.
Address edits invalidate connection state; the deck remains editable and persists normally. Existing
owned request cancellation, drain and client disposal remain in force at normal shutdown.

The new canonical notebook has no review bridge in this slice. The form explicitly says card
templates and notebook review are not yet available; it neither subscribes to nor loads a legacy
Store and does not present the unavailable template list as empty. Review session/transaction,
note navigation, deck review and template mutation entry points reject with that same message before
starting review network operations. The legacy constructor still accepts its existing borrowed Store
for isolated older library consumers; this is not a second production Store or a data migration.
`AppHost` activation of this factory belongs to the separate canonical Store composition change.

On Windows with .NET SDK 10.0.403, `dotnet build Cotabby.sln --configuration Release` passed with zero warnings
or errors. Portable App filter `AnkiReviewFeatureTests|AnkiReviewControllerTests|AnkiReviewSettingsInteractionTests`
passed 195/195; compiled WPF `AnkiReviewSettingsViewTests` passed 7/7. Tests use synthetic key/client
effects and isolated legacy Store fixtures only. The full `dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj
--configuration Release --no-build --no-restore` suite passed 1,230/1,230. A 520 × 700 offscreen Windows form was inspected;
no matching Mac screenshot was available. Physical keyboard/focus, DPAPI, actual Anki, native
shutdown and the future canonical review bridge remain separate acceptance work.
