# NAudio.Wasapi

Windows speech output uses unmodified NAudio.Wasapi and NAudio.Core version 3.3.0 as separate
managed assemblies. Copyright Mark Heath and contributors; MIT license, retained beside this notice.

Package: https://www.nuget.org/packages/NAudio.Wasapi/3.3.0
Corresponding source: https://github.com/naudio/NAudio/tree/4f8889e2bd34c3978a25652c6c172fa1502ccba3
(tag v3.3.0).

The port uses the low-level WASAPI wrappers for an event-driven shared output stream and raw stream
clock. No microphone, model, speech generation, hosted service or personal audio is supplied by
these dependencies.
