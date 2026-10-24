#include "SpeechPlatform.h"
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>
#include <TlHelp32.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <propvarutil.h>
#include <wrl/client.h>
#include <algorithm>
#include <climits>
#include <cstdio>
#include <fcntl.h>
#include <io.h>
#include <memory>
#include <stdexcept>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace {
struct HandleCloser { void operator()(void * value) const { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); } };
using Handle = std::unique_ptr<void, HandleCloser>;
std::string utf8(const wchar_t * value) {
    int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) throw std::runtime_error("invalid_arguments");
    std::string result(static_cast<size_t>(bytes), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value, -1, result.data(), bytes, nullptr, nullptr) != bytes)
        throw std::runtime_error("invalid_arguments");
    result.pop_back(); return result;
}
uint64_t creation(HANDLE process) {
    FILETIME created{}, exited{}, kernel{}, user{};
    if (!GetProcessTimes(process, &created, &exited, &kernel, &user)) throw std::runtime_error("invalid_parent");
    return (uint64_t(created.dwHighDateTime) << 32) | created.dwLowDateTime;
}
// COM/Media Foundation initialization is local to reference metadata admission. A scope balances
// every successful initialize, and no decoded waveform is requested before the duration is bounded.
struct MediaScope {
    bool com = false, media = false;
    MediaScope() { com = SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)); if (com) media = SUCCEEDED(MFStartup(MF_VERSION, MFSTARTUP_LITE)); }
    ~MediaScope() { if (media) MFShutdown(); if (com) CoUninitialize(); }
};
}
namespace speech_platform {
int64_t write_standard_output(const char * data, size_t length) {
    return _write(_fileno(stdout), data, static_cast<unsigned int>(std::min(length, size_t(INT_MAX))));
}
std::filesystem::path path_from_utf8(const std::string & value) { return std::filesystem::u8path(value); }
std::string path_utf8(const std::filesystem::path & value) { return value.u8string(); }
uint32_t parse_parent_id(const std::string & value) {
    const auto number = std::stoull(value);
    if (number > UINT32_MAX) throw std::runtime_error("invalid_arguments");
    return static_cast<uint32_t>(number);
}
void watch_parent(uint32_t expected) {
    Handle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    if (snapshot.get() == INVALID_HANDLE_VALUE) throw std::runtime_error("invalid_parent");
    PROCESSENTRY32W entry{}; entry.dwSize = sizeof(entry); bool matched = false;
    if (Process32FirstW(snapshot.get(), &entry)) do {
        if (entry.th32ProcessID == GetCurrentProcessId()) { matched = entry.th32ParentProcessID == expected; break; }
    } while (Process32NextW(snapshot.get(), &entry));
    if (!matched) throw std::runtime_error("invalid_parent");
    Handle parent(OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, expected));
    // A numeric PID can be reused after the launcher exits. Keep the exact kernel object and
    // reject any replacement born after this helper; never begin watching an unrelated process.
    if (!parent || creation(parent.get()) > creation(GetCurrentProcess()) || WaitForSingleObject(parent.get(), 0) != WAIT_TIMEOUT)
        throw std::runtime_error("invalid_parent");
    std::thread([owned = std::move(parent)] {
        DWORD result = WaitForSingleObject(owned.get(), INFINITE);
        ExitProcess(result == WAIT_OBJECT_0 ? 0 : 1);
    }).detach();
}
bool bounded_reference_duration(const std::filesystem::path & path) {
    MediaScope scope; if (!scope.media) return false;
    ComPtr<IMFAttributes> attributes;
    if (FAILED(MFCreateAttributes(&attributes, 1)) || FAILED(attributes->SetUINT32(MF_READWRITE_DISABLE_CONVERTERS, TRUE))) return false;
    ComPtr<IMFSourceReader> reader;
    if (FAILED(MFCreateSourceReaderFromURL(path.c_str(), attributes.Get(), &reader))) return false;
    PROPVARIANT duration{}; PropVariantInit(&duration);
    LONGLONG ticks = 0;
    HRESULT result = reader->GetPresentationAttribute(MF_SOURCE_READER_MEDIASOURCE, MF_PD_DURATION, &duration);
    if (SUCCEEDED(result)) result = PropVariantToInt64(duration, &ticks);
    PropVariantClear(&duration);
    return SUCCEEDED(result) && ticks > 0 && ticks <= 30LL * 10'000'000;
}
}

#ifndef LINGROVE_SPEECH_PLATFORM_CHECKS
int wmain(int argc, wchar_t ** argv) {
    // CRT text translation would change PCM bytes and can interpret0x1a as EOF. Configure
    // both binary pipes before even the --version frame; stderr is the parent's private NUL sink.
    if (_setmode(_fileno(stdin), _O_BINARY) < 0 || _setmode(_fileno(stdout), _O_BINARY) < 0) return 1;
    try {
        std::vector<std::string> storage; storage.reserve(argc);
        for (int i = 0; i < argc; ++i) storage.push_back(utf8(argv[i]));
        std::vector<char *> arguments; arguments.reserve(argc);
        for (auto & value : storage) arguments.push_back(value.data());
        return speech_main(argc, arguments.data());
    } catch (...) { return speech_main(0, nullptr); } // Reuse the source's fixed fatal envelope.
}

#endif
