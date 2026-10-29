# Windows native speech development build

On 2026-10-30 (Asia/Shanghai), this Windows x64 10.0.19045 host gained the minimal
Visual Studio 2022 Build Tools 17.14.42 C++ workload, x64 compiler, Windows SDK and CMake
components. No reboot, model download, installer generation or release was performed.
The actual tools were MSVC 19.44.35229, Windows SDK 10.0.26100.0, CMake 3.31.6-msvc6
and the bundled Python 3.12.14 interpreter.

The independent source at `D:\LingroveNativeBuild\llama.cpp` is pinned to
`2a667032dc788c3a071d538e0b572e45a3f12731` with the existing exact reviewed streaming patch.
The helper/platform source is unchanged between the original 43b reference and current Mac
`3b53402f2cbc196b0398d133177fd80006d165b2`; historical correspondence guards retain their pin.

Executed with the full paths of the installed CMake and bundled Python:

```powershell
python scripts/prepare-speech-runtime.py --source-dir D:\LingroveNativeBuild\llama.cpp
cmake -S native/tts -B D:\LingroveNativeBuild\cmake -G "Visual Studio 17 2022" -A x64 -DLLAMA_SOURCE_DIR=D:/LingroveNativeBuild/llama.cpp -DPython3_EXECUTABLE=<bundled-python>
python scripts/check-speech-vs-project.py --build-dir D:\LingroveNativeBuild\cmake --source-dir D:\LingroveNativeBuild\llama.cpp
cmake --build D:\LingroveNativeBuild\cmake --config Release --target lingrove-tts-helper lingrove-tts-platform-check --parallel 2 -- /warnAsError:MSB8027
python scripts/smoke-native-speech.py --helper D:\LingroveNativeBuild\cmake\Release\lingrove-tts-helper.exe --platform D:\LingroveNativeBuild\cmake\Release\lingrove-tts-platform-check.exe
```

Preparation and the Visual Studio source-ownership checker passed. Both real CPU targets built;
all **14 actual model-free framing/platform checks passed**. This includes owned synthetic
audio/file/process effects, with no model, user text, playback or endpoint invocation.

The development app output at
`src/Cotabby.App/bin/Release/net10.0-windows10.0.19041.0/win-x64/` now contains the helper
and both native license/notice files. The copied helper's SHA-256 matches its build output:
`A485EBFDC4996288DDBF3F2DB1056CE932DAA6A332F4677EBFA30075927B7342`.
This removes helper absence in that local development output; an ordinary fresh .NET build
still does not compile or fetch this independent executable. CI retains its separate native
compile/check job and does not publish binaries. Model-backed generation, CPU/GPU performance,
audible output and physical application shutdown remain for final host acceptance.
