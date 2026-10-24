#include "SpeechPlatform.h"
#include <AudioToolbox/AudioToolbox.h>
#include <unistd.h>
#include <sys/event.h>
#include <thread>
#include <cmath>
#include <stdexcept>

// This reference-side adapter exists only to compile/exercise the port's common helper on a
// Mac without editing the pinned Mac repository. The Windows app builds the Windows adapter.
namespace speech_platform {
int64_t write_standard_output(const char * data, size_t length) { return ::write(STDOUT_FILENO, data, length); }
std::filesystem::path path_from_utf8(const std::string & value) { return std::filesystem::u8path(value); }
std::string path_utf8(const std::filesystem::path & value) { return value.u8string(); }
uint32_t parse_parent_id(const std::string & value) {
    auto number = std::stoull(value); if (number > UINT32_MAX) throw std::runtime_error("invalid_arguments");
    return static_cast<uint32_t>(number);
}
void watch_parent(uint32_t parent) {
    if (getppid() != static_cast<pid_t>(parent)) throw std::runtime_error("invalid_arguments");
    std::thread([parent]() {
        const int queue = kqueue(); if (queue < 0) _exit(1);
        struct kevent change, event;
        EV_SET(&change, parent, EVFILT_PROC, EV_ADD | EV_ONESHOT, NOTE_EXIT, 0, nullptr);
        const int registration = kevent(queue, &change, 1, nullptr, 0, nullptr);
        if (getppid() != static_cast<pid_t>(parent)) _exit(0);
        if (registration != 0) _exit(1);
        if (kevent(queue, nullptr, 0, &event, 1, nullptr) > 0) _exit(0);
        _exit(1);
    }).detach();
}
bool bounded_reference_duration(const std::filesystem::path & path) {
    auto url = CFURLCreateFromFileSystemRepresentation(kCFAllocatorDefault,
        reinterpret_cast<const UInt8 *>(path.c_str()), path.string().size(), false);
    if (!url) return false;
    AudioFileID file = nullptr; const auto status = AudioFileOpenURL(url, kAudioFileReadPermission, 0, &file); CFRelease(url);
    if (status != noErr || !file) return false;
    Float64 duration = 0; UInt32 size = sizeof(duration);
    const auto result = AudioFileGetProperty(file, kAudioFilePropertyEstimatedDuration, &size, &duration); AudioFileClose(file);
    return result == noErr && std::isfinite(duration) && duration > 0 && duration <= 30;
}
}
#ifndef LINGROVE_SPEECH_PLATFORM_CHECKS
int main(int argc, char ** argv) { return speech_main(argc, argv); }
#endif
