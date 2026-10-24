#pragma once
#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <string>

// The helper owns its platform state until process exit. These narrow effects keep the complete
// original generation/framing loop shared, while Windows never interprets a UTF-8 path as ANSI.
namespace speech_platform {
int64_t write_standard_output(const char * data, size_t length);
std::filesystem::path path_from_utf8(const std::string & value);
std::string path_utf8(const std::filesystem::path & value);
uint32_t parse_parent_id(const std::string & value);
void watch_parent(uint32_t expected);
bool bounded_reference_duration(const std::filesystem::path & path);
}
int speech_main(int argc, char ** argv);
