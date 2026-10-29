# Unknown-source translation planning

This bounded prerequisite follows frozen Mac
`35b7fbe5c788c5524936ade1dfb7c89a3a285b04`:
`Lingrove/Support/LanguageLearning/LearningExpressionPrompt.swift`,
`LearningTranslationPrompt.swift`, `LearningTranslationBudget.swift`, and the nullable reading
source in `Lingrove/App/Coordinators/InlineFeatures/LearningReadingCoordinator.swift`.
The existing historical prompt fixtures retain their recorded references and expected bytes.

`LearningTranslationOptions.SourceLanguage` can now retain an unknown source as null. Writing
still defaults to Chinese and resolves an explicit source before capture. The request validator
continues to reject invalid known language values and known same-language directions. Generic
prompts omit the source-language name, Hy-MT keeps its target-only instructions, and TranslateGemma
uses the frozen unknown-source instruction without inventing a language code. Known-direction
prompt bytes, style rules, budgets, target bounds and independent runtime ownership are preserved.
The existing dictionary annotation path requires a known writing-source language rather than
silently substituting one.

The primary independently compared the changed source, nullable consumers and committed Mac
prompt bodies. The combined Windows Release solution built with zero warnings/errors. These
commands passed Core 40/40, portable App 5/5 and Inference 41/41:

```powershell
dotnet build Cotabby.sln -c Release --no-restore
dotnet test tests/Cotabby.Core.Tests/Cotabby.Core.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ReadingUnknownLanguageTests|FullyQualifiedName~LearningTranslationPromptTests|FullyQualifiedName~LearningTranslationBudgetTests|FullyQualifiedName~LearningTranslationRequestFactoryTests|FullyQualifiedName~LearningExpressionPromptTests'
dotnet test tests/Cotabby.App.Tests/Cotabby.App.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LearningDictionaryCardPolicyTests'
dotnet test tests/Cotabby.Inference.Tests/Cotabby.Inference.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~LearningLocalTranslationEngineTests|FullyQualifiedName~OpenAICompatibleLearningTranslationEngineTests'
```

The local planner test uses a fake runtime; the endpoint planner uses an in-memory handler and
asserts no network request or credential read during planning. No model or real endpoint is used.
This change does not activate reading capture, language detection, lookup, a reading card, shortcut,
OCR or clipboard acquisition. Those modules and physical Windows acceptance remain separate.
