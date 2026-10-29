# Explanation provider settings

Reference: Mac `3b53402f2cbc196b0398d133177fd80006d165b2`,
`Lingrove/UI/Settings/Panes/Engine/LearningModelConfigurationView.swift`,
`Lingrove/Models/LanguageLearning/LearningModelSettings.swift`, and
`Lingrove/Services/LanguageLearning/LearningModelController.swift`.

The Learning pane presents an independent explanation backend, its existing local model controls,
and an endpoint form with visible destination, model alias/discovery, explicit test, credential
Save/Clear, and per-destination context/output/temperature/top-p. A focused edit carries the
destination identity until commit. The metadata operation is owned by the shared explanation
endpoint feature through its final UI continuation; closing or changing the active snapshot retires
the result. The local view performs no key read or metadata request. Endpoint discovery is explicit
on Windows rather than automatic. The reviewed application composition connects the endpoint
generation factories separately; this control does not itself create a runtime. Errors do not render
credential, response body, or exception text.

Both learning tasks expose independent battery/plugged-in controls. When switching is enabled,
manual backend, local model/template and endpoint-model selectors are disabled and the current
power selection is displayed. Address and credentials remain role-scoped; parameter edits use
the active destination and reject commits from an earlier model. Translation's explicit power
endpoint selection uses the active consent projection without overwriting its manual preference.

The compiled WPF tests use fake credentials and metadata and exercise local privacy, explicit key
operations, stale results, focused edit identity, destination scope, errors and rendered layout.
They do not contact a service or load a model. WPF rendering is a component check; physical desktop,
screen reader and live endpoint quality remain separate acceptance work. AppHost now injects both
request-owned endpoint factories into the single explanation controller; the settings pane does not
construct a second controller or HTTP pool.
The synthetic endpoint view was rendered and inspected at 450×480 logical pixels and 96 DPI;
its narrow layout wraps the discovered-model control under the model field. The test uses a plain
white WPF host, so it is not a full theme or physical desktop review. No matching Mac screenshot was
available; hierarchy follows the committed source rather than a pixel comparison.
