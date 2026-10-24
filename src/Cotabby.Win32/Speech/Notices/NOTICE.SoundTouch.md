# SoundTouch.Net

The Windows speech renderer uses the unmodified `SoundTouch.Net` NuGet package, pinned to 2.3.2.
Its managed assembly remains a separate dependency. No native speech models or audio are included.

SoundTouch audio processing library, copyright Olli Parviainen.
C# port, copyright Olaf Woudenberg.
Licensed under GNU LGPL version 2.1 or later; see `LICENSE.SoundTouch.txt` in this directory.

Corresponding package source: https://github.com/owoudenberg/soundtouch.net/tree/98e5b8fd2f8efed0ddf7c8f66b435bfb231659dc
(tag `2.3.2`). Package: https://www.nuget.org/packages/SoundTouch.Net/2.3.2

This dependency supplies Windows' pitch-preserving tempo effect in place of Apple's
AVAudioUnitTimePitch. It does not implement speech generation, language recognition or a device.
