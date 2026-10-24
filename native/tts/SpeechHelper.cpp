// Copyright (c) 2026 Lingrove contributors.
// The audio-generation sequence is adapted from llama.cpp tools/tts/tts.cpp.
// llama.cpp is Copyright (c) 2023-2026 The ggml authors, licensed under MIT.
// See LLAMA_LICENSE.txt shipped alongside this source and the application licenses.
#include "SpeechPlatform.h"
#include "common.h"
#include "sampling.h"
#include "llama.h"
#include "mtmd.h"
#include "mtmd-helper.h"
#include <nlohmann/json.hpp>
#include <algorithm>
#include <chrono>
#include <cerrno>
#include <cmath>
#include <cctype>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <random>
#include <set>
#include <stdexcept>
#include <string>
#include <thread>

using json = nlohmann::json;
namespace fs = std::filesystem;

// One process owns one model pair and one mutable llama context. The parent sends sequential
// requests, terminates the process to cancel inference, and unloads it after an idle timeout.
// stdout is exclusively framed headers/PCM. Blocking pipe writes provide bounded backpressure;
// the parent can still cancel this process while playback is draining queued audio.
static constexpr size_t max_line = 16 * 1024;
static constexpr size_t max_audio = 32 * 1024 * 1024;

static void write_stdout(const char * data, size_t length) {
    while (length) {
        const auto count = speech_platform::write_standard_output(data, length);
        if (count < 0 && errno == EINTR) continue;
        if (count <= 0) throw std::runtime_error("pipe_closed");
        data += count;
        length -= static_cast<size_t>(count);
    }
}
static void emit(const json & value, const char * pcm = nullptr, size_t length = 0) {
    const auto header = value.dump();
    if (header.size() > 4096 || length > 262144) throw std::runtime_error("frame_too_large");
    const uint32_t size = static_cast<uint32_t>(header.size());
    const char prefix[]{char(size >> 24), char(size >> 16), char(size >> 8), char(size)};
    write_stdout(prefix, 4);
    write_stdout(header.data(), header.size());
    if (length) write_stdout(pcm, length);
}
static bool bounded_line(std::string & line) {
    line.clear();
    bool oversized = false;
    for (int c; (c = std::getchar()) != EOF;) {
        if (c == '\n') {
            if (oversized) throw std::runtime_error("request_too_large");
            return true;
        }
        if (line.size() < max_line) line.push_back(static_cast<char>(c));
        else oversized = true;
    }
    if (oversized) throw std::runtime_error("request_too_large");
    return !line.empty();
}

static std::string field(const json & value, const char * name, size_t limit, bool optional = false) {
    if (optional && (!value.contains(name) || value[name].is_null())) return {};
    if (!value.contains(name) || !value[name].is_string()) throw std::runtime_error("invalid_request");
    auto text = value[name].get<std::string>();
    if ((!optional && text.empty()) || text.size() > limit || text.find('\0') != std::string::npos)
        throw std::runtime_error("invalid_request");
    return text;
}

static fs::path model_path(const std::string & path) {
    fs::path result = fs::canonical(speech_platform::path_from_utf8(path));
    if (!fs::is_regular_file(result) || result.extension() != ".gguf")
        throw std::runtime_error("invalid_model");
    char magic[4]{};
    std::ifstream input(result, std::ios::binary);
    input.read(magic, 4);
    if (input.gcount() != 4 || std::string(magic, 4) != "GGUF") throw std::runtime_error("invalid_model");
    return result;
}


int speech_main(int argc, char ** argv) {
    if (argc == 2 && std::string(argv[1]) == "--version") {
        emit({{"protocol_version", 2}, {"runtime", "llama.cpp"},
              {"revision", "2a667032dc788c3a071d538e0b572e45a3f12731"}});
        return 0;
    }
    try {
        std::string model, projection;
        uint32_t parent = 0;
        for (int i = 1; i < argc; i += 2) {
            if (i + 1 == argc) throw std::runtime_error("invalid_arguments");
            std::string option = argv[i];
            if (option == "--model") model = argv[i + 1];
            else if (option == "--mmproj") projection = argv[i + 1];
            else if (option == "--parent-pid") parent = speech_platform::parse_parent_id(argv[i + 1]);
            else throw std::runtime_error("invalid_arguments");
        }
        if (model.empty() || projection.empty() || parent < 2)
            throw std::runtime_error("invalid_arguments");
        speech_platform::watch_parent(parent);
        common_init();
        llama_backend_init();
        common_params params;
        params.model.path = speech_platform::path_utf8(model_path(model));
        params.mmproj.path = speech_platform::path_utf8(model_path(projection));
        params.embedding = true;
        params.pooling_type = LLAMA_POOLING_TYPE_NONE;
        params.n_ctx = 4096;
        params.n_batch = 256;
        params.n_ubatch = 256;
        params.n_gpu_layers = 99;
        params.cpuparams.n_threads = 4;
        params.cpuparams_batch.n_threads = 4;
        params.sampling.seed = 42;
        auto loaded = common_init_from_params(params);
        auto * model_ptr = loaded->model();
        auto * context = loaded->context();
        if (!model_ptr || !context) throw std::runtime_error("model_load_failed");
        mtmd_context_params audio_params = mtmd_context_params_default();
        audio_params.use_gpu = true;
        mtmd::context_ptr audio(mtmd_init_from_file(params.mmproj.path.c_str(), model_ptr, audio_params));
        if (!audio || mtmd_gen_audio_get_info(audio.get()).type != MTMD_GEN_AUDIO_TYPE_QWEN3TTS)
            throw std::runtime_error("unsupported_model");
        emit({{"event", "ready"}, {"protocol_version", 2}});

        // set_input resets request buffers while retaining the immutable embedding cache.
        mtmd_helper::gen_audio generator(context, audio.get());
        std::string line;
        for (;;) {
            json id = nullptr;
            try {
                if (!bounded_line(line)) break;
                auto request = json::parse(line, nullptr, false);
                if (!request.is_object()) throw std::runtime_error("invalid_request");
                id = field(request, "id", 64);
                const auto type = field(request, "type", 32);
                if (type == "shutdown") break;
                if (type != "synthesize") throw std::runtime_error("invalid_request");
                auto text = field(request, "text", 4096);
                auto language = field(request, "language", 8);
                const std::set<std::string> languages{"en", "zh", "ja", "ko", "de", "fr", "ru", "pt", "es", "it"};
                if (!languages.count(language)) throw std::runtime_error("unsupported_language");
                int max_frames = request.value("max_frames", 512);
                if (max_frames < 1 || max_frames > 512) throw std::runtime_error("invalid_request");
                const int chunk_frames = request.value("chunk_frames", 8);
                if (chunk_frames < 4 || chunk_frames > 72) throw std::runtime_error("invalid_request");
                // Request options never mutate the loaded session defaults. Validate again at
                // the process boundary, even though Swift has already normalized UI preferences.
                auto sampling = params.sampling;
                auto real = [&](const char * key, double fallback, double low, double high) {
                    if (!request.contains(key)) return fallback;
                    if (!request[key].is_number()) throw std::runtime_error("invalid_request");
                    const double value = request[key].get<double>();
                    if (!std::isfinite(value) || value < low || value > high) throw std::runtime_error("invalid_request");
                    return value;
                };
                sampling.temp = real("temperature", sampling.temp, 0, 2);
                sampling.top_p = real("top_p", sampling.top_p, 0.01, 1);
                sampling.penalty_repeat = real("repetition_penalty", sampling.penalty_repeat, 1, 2);
                const double top_k = real("top_k", sampling.top_k, 1, 256);
                const double seed = real("seed", sampling.seed, 0, UINT32_MAX);
                if (std::floor(top_k) != top_k || std::floor(seed) != seed) throw std::runtime_error("invalid_request");
                sampling.top_k = static_cast<int>(top_k);
                sampling.seed = static_cast<uint32_t>(seed);
                // Share one concrete seed between talker and codec for this operation. Reusing a
                // helper must still restart a fixed-seed codec at every utterance boundary.
                if (sampling.seed == UINT32_MAX) sampling.seed = std::random_device{}() % UINT32_MAX;
                auto speaker_path = field(request, "speaker_audio_path", 1024, true);
                mtmd::bitmap_ptr speaker;
                if (!speaker_path.empty()) {
                    auto path = fs::canonical(speech_platform::path_from_utf8(speaker_path));
                    auto extension = speech_platform::path_utf8(path.extension());
                    std::transform(extension.begin(), extension.end(), extension.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
                    if ((extension != ".wav" && extension != ".mp3") || !fs::is_regular_file(path)
                        || fs::file_size(path) > 10 * 1024 * 1024) throw std::runtime_error("invalid_speaker_audio");
                    // Bound decoded samples before mtmd allocates the reference waveform. File
                    // size alone cannot bound a long low-bitrate MP3's expansion into float PCM.
                    if (!speech_platform::bounded_reference_duration(path)) throw std::runtime_error("invalid_speaker_audio");
                    const auto speaker_utf8 = speech_platform::path_utf8(path);
                    auto decoded = mtmd_helper_bitmap_init_from_file(audio.get(), speaker_utf8.c_str(), false,
                        mtmd_helper_init_opt_default());
                    if (!decoded.bitmap) throw std::runtime_error("invalid_speaker_audio");
                    speaker.reset(decoded.bitmap);
                }

                // Reset native state between utterances; only the immutable model weights survive.
                llama_memory_clear(llama_get_memory(context), true);
                std::unique_ptr<common_sampler, decltype(&common_sampler_free)> sampler(
                    common_sampler_init(model_ptr, sampling), common_sampler_free);
                if (!sampler) throw std::runtime_error("generation_failed");
                mtmd_helper_gen_audio_inp input{};
                input.seq_id = 0;
                input.prompt = text.c_str(); input.prompt_len = text.size();
                input.speaker_ref = speaker.get(); input.lang = language.c_str();
                input.top_k = sampling.top_k; input.top_p = sampling.top_p;
                input.seed = sampling.seed; input.out_type = MTMD_HELPER_GEN_AUDIO_OUTTYPE_PCM;
                mtmd_gen_audio_reset_rng(audio.get(), sampling.seed);
                if (generator.set_input(&input) != 0) throw std::runtime_error("generation_failed");
                for (;;) {
                    int status = generator.step_prompt(params.n_batch);
                    if (status < 0) throw std::runtime_error("generation_failed");
                    if (status == 0) break;
                }
                auto sample = [&]() {
                    auto token = common_sampler_sample(sampler.get(), context, -1);
                    common_sampler_accept(sampler.get(), token, true);
                    return token;
                };
                auto token = sample();
                const float * state = llama_get_embeddings_ith(context, -1);
                bool ended = false;
                int32_t rate = 0;
                size_t emitted_samples = 0;
                int sequence = 0;
                auto deliver = [&]() {
                    const char * bytes = nullptr; size_t size = 0; int64_t samples = 0;
                    // get_output flushes only newly buffered codes, retaining decoder state.
                    // Its PCM view is cumulative: publish each sample exactly once, before
                    // the next step can invalidate the view. Never copy or serialize old PCM.
                    if (generator.get_output(&rate, &bytes, &size, &samples) != 0 || !bytes
                        || size > max_audio || rate < 8000 || rate > 192000 || samples < 0
                        || samples > static_cast<int64_t>(rate) * 60 || size != size_t(samples) * sizeof(float)
                        || size_t(samples) < emitted_samples) throw std::runtime_error("invalid_audio");
                    while (emitted_samples < size_t(samples)) {
                        const size_t count = std::min(size_t(samples) - emitted_samples, size_t(16384));
                        const auto * pcm = reinterpret_cast<const float *>(bytes) + emitted_samples;
                        for (size_t i = 0; i < count; ++i)
                            if (!std::isfinite(pcm[i]) || std::abs(pcm[i]) > 8) throw std::runtime_error("invalid_audio");
                        emit({{"event", "chunk"}, {"id", id}, {"sequence", sequence++}, {"sample_rate", rate},
                              {"samples", count}, {"payload_bytes", count * sizeof(float)}, {"channels", 1}, {"format", "f32le"}},
                              reinterpret_cast<const char *>(pcm), count * sizeof(float));
                        emitted_samples += count;
                    }
                };
                for (int frame = 0; frame < max_frames; ++frame) {
                    const float * next = nullptr;
                    if (generator.step_gen(token, state, &next, &ended) != 0)
                        throw std::runtime_error("generation_failed");
                    if (ended || !next) { ended = true; break; }
                    if ((frame + 1) % chunk_frames == 0) deliver();
                    state = next;
                    token = sample();
                }
                if (!ended) throw std::runtime_error("speech_too_long");
                deliver();
                if (!emitted_samples) throw std::runtime_error("invalid_audio");
                emit({{"event", "result"}, {"id", id}, {"ok", true}, {"samples", emitted_samples},
                      {"sample_rate", rate}});
            } catch (const std::exception &) {
                // Never include exception.what(): JSON parse errors can contain the user's text.
                emit({{"event", "result"}, {"id", id}, {"ok", false},
                      {"error", {{"code", "synthesis_failed"}, {"message", "Speech generation failed or exceeded its limits."}}}});
            }
        }
        return 0;
    } catch (const std::exception &) {
        emit({{"event", "fatal"}, {"error", {{"code", "runtime_unavailable"},
              {"message", "The local speech runtime could not load this model pair."}}}});
        return 1;
    }
}
