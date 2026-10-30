#requires -Version 5.1
<#
Build an unpackaged Windows x64 development app. Local speech is an explicit source build;
it restores only the pinned dependency, runs model-free checks, then embeds the checked helper.
No product launch, model acquisition, installer, upload or machine configuration is performed.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$IncludeLocalSpeech,
    [string]$NativeBuildRoot,
    [string]$PythonExecutable,
    [string]$CMakeExecutable,
    [ValidateRange(1, 32)][int]$NativeJobs = 2
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Build step failed ($LASTEXITCODE): $Executable" }
}

function Resolve-Tool([string]$ExplicitPath, [string]$CommandName) {
    if ($ExplicitPath) {
        $item = Get-Item -LiteralPath $ExplicitPath
        if ($item.PSIsContainer) { throw "Expected an executable file: $ExplicitPath" }
        return $item.FullName
    }
    $command = Get-Command $CommandName -All -CommandType Application -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -notmatch '\\WindowsApps\\' } | Select-Object -First 1
    if ($command) { return $command.Source }
    return $null
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or
    [Environment]::Is64BitProcess -ne $true -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
    throw 'Run in a 64-bit PowerShell process on Windows x64.'
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$appDirectory = Join-Path $repoRoot "src\Cotabby.App\bin\$Configuration\net10.0-windows10.0.19041.0\win-x64"
$localDotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet -PathType Leaf) { Resolve-Tool $localDotnet 'dotnet' } else { Resolve-Tool '' 'dotnet' }
if (-not $dotnet) { throw 'Install the SDK selected by global.json before building.' }

# Resolve native tools before compiling the managed app; Windows Store aliases and Python
# install-manager launchers are not invoked, because setup must not silently install tools.
if ($IncludeLocalSpeech) {
    $python = Resolve-Tool $PythonExecutable 'python'
    if (-not $python) { throw 'Pass -PythonExecutable with an installed Python 3.11+ executable.' }
    Invoke-Checked $python @('-c', 'import sys; assert sys.version_info >= (3, 11), "Python 3.11+ is required"')
    $cmake = Resolve-Tool $CMakeExecutable 'cmake'
    if (-not $cmake) {
        $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
            $installation = & $vswhere -latest -products '*' -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if ($LASTEXITCODE -eq 0 -and $installation) {
                $candidate = Join-Path ($installation | Select-Object -First 1) 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { $cmake = $candidate }
            }
        }
    }
    if (-not $cmake) { throw 'Pass -CMakeExecutable or install the VS 2022 C++ CMake tools.' }
    if (-not $NativeBuildRoot) { $NativeBuildRoot = Join-Path $repoRoot 'build\speech-native' }
    $NativeBuildRoot = [IO.Path]::GetFullPath($NativeBuildRoot)
    $sourceDirectory = Join-Path $NativeBuildRoot 'llama.cpp'
    $cmakeDirectory = Join-Path $NativeBuildRoot 'cmake'
}

$previousTelemetry = [Environment]::GetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', 'Process')
Push-Location $repoRoot
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    Invoke-Checked $dotnet @('build', 'src/Cotabby.App/Cotabby.App.csproj', '--configuration', $Configuration)
    if ($IncludeLocalSpeech) {
        Invoke-Checked $python @('scripts/prepare-speech-runtime.py', '--source-dir', $sourceDirectory)
        Invoke-Checked $cmake @('-S', 'native/tts', '-B', $cmakeDirectory, '-G', 'Visual Studio 17 2022', '-A', 'x64', "-DLLAMA_SOURCE_DIR=$sourceDirectory", "-DPython3_EXECUTABLE=$python")
        Invoke-Checked $python @('scripts/check-speech-vs-project.py', '--build-dir', $cmakeDirectory, '--source-dir', $sourceDirectory)
        Invoke-Checked $cmake @('--build', $cmakeDirectory, '--config', $Configuration, '--target', 'lingrove-tts-helper', 'lingrove-tts-platform-check', '--parallel', "$NativeJobs", '--', '/warnAsError:MSB8027')
        $helper = Join-Path $cmakeDirectory "$Configuration\lingrove-tts-helper.exe"
        $platform = Join-Path $cmakeDirectory "$Configuration\lingrove-tts-platform-check.exe"
        Invoke-Checked $python @('scripts/smoke-native-speech.py', '--helper', $helper, '--platform', $platform)
        $helperDirectory = Join-Path $appDirectory 'Helpers'
        [IO.Directory]::CreateDirectory($helperDirectory) | Out-Null
        Copy-Item -LiteralPath $helper -Destination (Join-Path $helperDirectory 'lingrove-tts-helper.exe') -Force
        foreach ($notice in @('THIRD_PARTY_NOTICES.txt', 'LLAMA_LICENSE.txt', 'llama-revision.txt')) {
            Copy-Item -LiteralPath (Join-Path $repoRoot "native\tts\$notice") -Destination (Join-Path $helperDirectory $notice) -Force
        }
        if ((Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath (Join-Path $helperDirectory 'lingrove-tts-helper.exe') -Algorithm SHA256).Hash) {
            throw 'The app-local helper differs from the checked build.'
        }
    }
    Write-Host ("Development app: " + (Join-Path $appDirectory 'Cotabby.exe'))
    if (-not $IncludeLocalSpeech) { Write-Host 'Local speech helper was not rebuilt; use -IncludeLocalSpeech to build and embed it.' }
} finally {
    Pop-Location
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', $previousTelemetry, 'Process')
}
