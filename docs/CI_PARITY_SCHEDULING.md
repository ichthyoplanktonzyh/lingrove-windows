# Bounded historical source verification

The macOS source job previously invoked 68 standalone verifier commands sequentially.
PR 139's successful job took 44m30s from job start to completion; its longest individual
verifier was the graph solver at 116s. This is CI time, not native product acceptance.

`scripts/macos-parity-suite.json` preserves the 68 names, scripts and reference checkout
arguments from `9dc7b0c` in their original order. Their command-list SHA-256 is
`2d2053af74fe3fc5bedad2a8b366b96dd1d3be8dcdea49b00e11f09a93379323`.
No original verifier body, fixture, revision or assertion changes. The versioned editor
checkout moves before the suite; the original baseline and editor pins remain distinct.

`run_macos_parity_suite.py` launches at most two Python verifier processes. Each existing
verifier already creates its own temporary source/binary/files; reviewed preference probes
use UUID suite names. Git reference reads are read-only. The scheduler runs every command
once, retains every exit status and returns failure for any failure or missing result.
It groups each command's diagnostics in the CI log, capped to the last 1 MiB per command.
There is no artifact upload or cache-based bypass. Both serial (`--jobs 1`) and bounded
parallel (`--jobs 2`) execution remain available; greater concurrency is rejected.

SIGINT/SIGTERM close launch admission, cancel queued tasks and terminate only registered
owned process groups on macOS, including compiler/helper descendants. Leaders are joined
before temporary output files are closed. Repeated cancellation cannot interrupt cleanup.
The six portable tests cover exact command retention, invalid/duplicate inputs, concurrent
overlap and its cap, all failures continuing to gate the result, serial execution, abort
drain and an actual synthetic child being terminated and joined. They do not run Swift
on Windows. Native source verifiers remain macOS-only.

The CI workflow still has four validation jobs, keeps the 75-minute source-job limit and
retains the separate frozen vocabulary/latest CLI probes, Windows tests and native helper
build. It downloads no model, creates no installer/package/release and uploads no artifact.
The source-job throughput change requires an actual successful macOS run before acceptance;
no measured speedup is claimed from the Windows scheduler tests.

Both final-head CI runs subsequently passed all four jobs. The sequential #140 macOS job took
2,650s (44m10s), and #141 took 1,538s (25m38s), with its complete 68-verifier parallel step taking
1,222s (20m22s). This pair saved 18m32s of job time; hosted-runner variation still applies and
these two observations are not a guaranteed duration for later runs. The verifier bodies,
commands, assertions and source pins were identical in that comparison.
