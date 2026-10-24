// Test-only native effect harness: no llama model, device playback or application entrypoint.
#include "SpeechPlatform.h"
#include <iostream>
#include <stdexcept>
#include <thread>
#include <chrono>

static int run(const std::string & mode, const std::filesystem::path & path, uint32_t parent) {
    if (mode == "reference") { std::cout << (speech_platform::bounded_reference_duration(path) ? "true" : "false") << std::endl; return 0; }
    if (mode == "watch") {
        speech_platform::watch_parent(parent); std::cout << "watching" << std::endl;
        // EOF must not be an alternative reason to exit in the abrupt-parent regression.
        for (;;) std::this_thread::sleep_for(std::chrono::hours(1));
    }
    if (mode != "values") return 2;
    const std::string literal = u8"语音 space/backslash\\quote\".wav";
    if (speech_platform::path_utf8(speech_platform::path_from_utf8(literal)) != literal) throw std::runtime_error("UTF8 round trip differs");
    if (speech_platform::parse_parent_id("4294967295") != UINT32_MAX || speech_platform::parse_parent_id("42") != 42) throw std::runtime_error("DWORD parent differs");
    for (const auto * invalid : {"4294967296", "not-a-pid", "-2"}) {
        bool rejected = false; try { (void)speech_platform::parse_parent_id(invalid); } catch (...) { rejected = true; }
        if (!rejected) throw std::runtime_error("invalid parent admitted");
    }
    std::cout << "values" << std::endl; return 0;
}
#ifdef _WIN32
int wmain(int argc, wchar_t ** argv) {
    try {
        if (argc == 2 && std::wstring(argv[1]) == L"values") return run("values", {}, 0);
        if (argc == 3 && std::wstring(argv[1]) == L"reference") return run("reference", std::filesystem::path(argv[2]), 0);
        if (argc == 3 && std::wstring(argv[1]) == L"watch") return run("watch", {}, static_cast<uint32_t>(std::stoull(argv[2])));
    } catch (...) { return 1; } return 2;
}
#else
int main(int argc, char ** argv) {
    try {
        if (argc == 2) return run(argv[1], {}, 0);
        if (argc == 3 && std::string(argv[1]) == "reference") return run("reference", std::filesystem::path(argv[2]), 0);
        if (argc == 3 && std::string(argv[1]) == "watch") return run("watch", {}, static_cast<uint32_t>(std::stoull(argv[2])));
    } catch (...) { return 1; } return 2;
}
#endif
