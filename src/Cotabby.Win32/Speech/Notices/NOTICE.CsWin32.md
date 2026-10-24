# Windows SDK interop generation

Speech decoding uses Microsoft.Windows.CsWin32 0.3.367 as a private build-time generator.
The restored package names source commit 6f3334302499fcbfea98ec3b433da1b2c49bfb47:
https://github.com/microsoft/CsWin32/tree/6f3334302499fcbfea98ec3b433da1b2c49bfb47

The generator's complete MIT license is retained beside this notice. Its upstream tool notices
are available at https://github.com/microsoft/CsWin32/blob/6f3334302499fcbfea98ec3b433da1b2c49bfb47/NOTICE.txt.
The generator and its tooling dependencies are not application runtime dependencies.

SDK declarations are generated from the package's pinned development metadata
Microsoft.Windows.SDK.Win32Metadata 71.0.37-preview, under that package's Windows SDK development
license. Only generated interop declarations compile into Cotabby.Win32; the metadata, SDK/tool
assemblies and generated source files are not copied into application output or this repository.
Native Media Foundation and Shell implementations are supplied by the Windows installation.
