"""Reviewed mechanical platform seam; generation, sampling, framing and errors stay original."""
from macos_vocabulary_reference import source
HELPER='native/tts/SpeechHelper.cpp'

def adapted_helper(repo):
 value=source(repo,HELPER)
 for header in ['#include <AudioToolbox/AudioToolbox.h>\n','#include <fcntl.h>\n','#include <unistd.h>\n','#include <sys/event.h>\n']:
  assert value.count(header)==1;value=value.replace(header,'')
 value=value.replace('#include <cmath>','#include <cmath>\n#include <cctype>',1)
 value=value.replace('#include "common.h"','#include "SpeechPlatform.h"\n#include "common.h"',1)
 changes=[('::write(STDOUT_FILENO, data, length)','speech_platform::write_standard_output(data, length)'),
  ('fs::canonical(path)','fs::canonical(speech_platform::path_from_utf8(path))'),
  ('int main(int argc, char ** argv)','int speech_main(int argc, char ** argv)'),
  ('pid_t parent = -1;','uint32_t parent = 0;'),
  ('static_cast<pid_t>(std::stol(argv[i + 1]))','speech_platform::parse_parent_id(argv[i + 1])'),
  ('parent < 2 || getppid() != parent','parent < 2'),
  ('model_path(model).string()','speech_platform::path_utf8(model_path(model))'),
  ('model_path(projection).string()','speech_platform::path_utf8(model_path(projection))'),
  ('fs::canonical(speaker_path)','fs::canonical(speech_platform::path_from_utf8(speaker_path))'),
  ('path.extension().string()','speech_platform::path_utf8(path.extension())'),
  ('extension.begin(), ::tolower','extension.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); }'),
  ('!bounded_reference_duration(path)','!speech_platform::bounded_reference_duration(path)'),
  ('auto decoded = mtmd_helper_bitmap_init_from_file(audio.get(), path.c_str(), false,','const auto speaker_utf8 = speech_platform::path_utf8(path);\n                    auto decoded = mtmd_helper_bitmap_init_from_file(audio.get(), speaker_utf8.c_str(), false,')]
 for before,after in changes:
  assert value.count(before)==1,before;value=value.replace(before,after)
 start=value.index('static bool bounded_reference_duration(');end=value.index('\nint speech_main(',start);value=value[:start]+value[end:]
 start=value.index('        // The expected PID comes from the app,');end=value.index('        common_init();',start);value=value[:start]+'        speech_platform::watch_parent(parent);\n'+value[end:]
 return value
