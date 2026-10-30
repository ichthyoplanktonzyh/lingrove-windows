# OLE drop callback and helper lifetime

The auxiliary-interface full regression at `19c30bc` exposed an existing cross-process drop failure:
the synthetic PNG/HGLOBAL case read its exact payload successfully, then `IDropTarget.Drop` failed
with RPC `0x800706BE`. The existing test correctly asserts both payload bytes and a successful RPC;
neither oracle is removed or retried to turn that run green.

## Diagnosis

The focused diagnostic scope is one xUnit theory (two transport cases) and its `PngData`,
`InvokeDrop`, `ReadLine` and process fixtures. Its exact-byte, call-result, effect and read-count
assertions are meaningful. The helper had one High lifecycle issue, not a missing test assertion:
`Target.Drop` published its frame and posted owner-thread WM_QUIT inside the callback. Its plain
managed COM callable wrapper was free-threaded, despite `Program.Main`/`OleInitialize` using STA.
The owner loop could therefore exit and release OLE while an off-thread callback/RPC still unwound.
The observation is consistent with that race; it is not a claim that every RPC failure has this cause.

Microsoft documents that [COM-exposed managed objects are normally free-threaded](https://learn.microsoft.com/en-us/dotnet/standard/threading/managed-and-unmanaged-threading-in-windows),
and that [StandardOleMarshalObject uses the standard OLE STA marshaler](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.standardolemarshalobject?view=net-10.0).
Creating a managed object on STA alone does not provide the required dispatch guarantee.

## Shared production correction

`Cotabby.ImageTransferHost/OleDropWindow.Target` now derives from `StandardOleMarshalObject`.
All OLE methods verify their native owner thread. Drop still emits the early busy marker and reads
the bounded foreign data once, but stages its result instead of publishing completion in the call.
A private message is handled by the owner's outer loop after callback dispatch has unwound; only
there is the unchanged image frame written, followed by ordinary one-shot cleanup.

An impossible off-thread Drop or failed completion post terminates only this isolated helper with
fixed code 2. Closing its pipe completes the parent's read as a failure rather than stranding a
pre-DROP/payload wait. It publishes no accepted image. The parent retains its kill-on-close Job,
joined retirement, asynchronous positioning, provider deadline and no-retry/store admission rules.
Diagnostic and normal modes share this correction; no test-only sleep, retry, STOP exemption or
weaker RPC assertion is introduced.

This Windows adapter follows the existing Mac image-drop responsibility recorded in the canonical
image/drop handoffs at their explicit pins. It changes no Mac reference, frame grammar, accepted
format, byte/pixel budget, image preparation, current-format identity, Store owner or transaction.

The focused 16-case OLE prototype suite passed after the correction, including synthetic PNG/GDI,
canonical editor/Store saves and held-provider retirement. Further independent final-head and CI
results belong to the PR record. These are owned synthetic processes/data; physical Explorer/browser
drop producers, virtual-file variants and the maintainer's final desktop acceptance remain separate.
