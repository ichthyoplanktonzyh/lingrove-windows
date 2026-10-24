# Generated codec input

`generated-speech-tone.mp3` contains our synthetic 440Hz tone, with no speech or third-party audio.
It is a 1776-byte decoder fixture, not a playback demo or a downloadable application artifact.
SHA256: `f3d1b676b534c6ed7e4586bf7bf2005e476b58490d1b34a59e3aba72855ca574`.

Created with FFmpeg8.1.1 and its libmp3lame encoder:

```sh
ffmpeg -v error -f lavfi -i 'sine=frequency=440:sample_rate=24000:duration=0.2' \
  -map_metadata -1 -c:a libmp3lame -b:a 48k -write_xing 1 -id3v2_version 0 \
  -y generated-speech-tone.mp3
```

Ordinary CI reads the committed bytes; it does not install FFmpeg or regenerate/download audio.
Encoder padding may make native decoded duration differ slightly from the nominal0.2seconds.
Tests measure actual decoded frames and tone energy rather than treating this file's header as
proof of successful decoding. Stereo WAV inputs and invalid/overlong inputs are built in memory.
