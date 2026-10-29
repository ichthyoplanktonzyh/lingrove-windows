#requires -Version 5.1
<#
Read-only development diagnostics. This script installs nothing, changes no machine settings,
starts no app/model and reads no user notebook. Run from a cloned Windows checkout.
#>
[CmdletBinding()]
param([switch]$RequireNative, [switch]$Json)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Check([string]$CheckName, [bool]$Available, [string]$Detail, [bool]$Required = $true) {
    $statusLabel = if ($Available) { 'ready' } elseif ($Required) { 'missing' } else { 'optional' }
    $checks.Add([pscustomobject]@{ Check = $CheckName; Status = $statusLabel; Detail = $Detail })
    if (-not $Available -and $Required) { $failures.Add($CheckName) }
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    Write-Error 'Run this check in PowerShell on the Windows development computer.'
    exit 1
}

$root = Split-Path $PSScriptRoot -Parent
$previousDotnetTelemetry = [Environment]::GetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', 'Process')
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $root
try {
    $hostArchitecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    Add-Check 'Windows x64 host' ($hostArchitecture -eq 'AMD64') $hostArchitecture
    $gitTool = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Check 'Git' ($null -ne $gitTool) $(if ($gitTool) { $gitTool.Source } else { 'Install Git for Windows.' })
    if ($gitTool) {
        $commit = & $gitTool.Source rev-parse --short HEAD 2>$null
        Add-Check 'Git checkout' ($LASTEXITCODE -eq 0) ($commit -join ' ')
    }

    $pin = Get-Content (Join-Path $root 'global.json') -Raw | ConvertFrom-Json
    $localDotnet = Join-Path $root '.dotnet\dotnet.exe'
    $dotnetTool = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $dotnetPath = if (Test-Path $localDotnet -PathType Leaf) { $localDotnet } elseif ($dotnetTool) { $dotnetTool.Source } else { $null }
    if ($dotnetPath) {
        try {
            $selectedSdk = & $dotnetPath --version 2>$null
            $sdkReady = $LASTEXITCODE -eq 0
        } catch { $selectedSdk = ''; $sdkReady = $false }
        Add-Check 'Repository SDK' $sdkReady ("global.json: {0}; selected: {1}" -f $pin.sdk.version, ($selectedSdk -join ' '))
        $runtimes = & $dotnetPath --list-runtimes 2>$null
        Add-Check 'WPF runtime 10' ($LASTEXITCODE -eq 0 -and [bool]($runtimes -match '^Microsoft.WindowsDesktop.App 10\.')) 'Microsoft.WindowsDesktop.App 10.x is required by the WPF app.'
    } else {
        Add-Check 'Repository SDK' $false ("Install .NET SDK compatible with global.json ({0}); do not change the pin to bypass setup." -f $pin.sdk.version)
        Add-Check 'WPF runtime 10' $false 'Install the Windows x64 .NET SDK/desktop runtime.'
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $nativeIde = if (Test-Path $vswhere -PathType Leaf) {
        & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    } else { $null }
    Add-Check 'MSVC x64 tools' ([bool]$nativeIde) $(if ($nativeIde) { $nativeIde -join ' ' } else { 'Add Desktop development with C++ (including Windows SDK).' }) ([bool]$RequireNative)
    $sdkRoots = @(
        [Microsoft.Win32.Registry]::GetValue('HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', $null)
        [Microsoft.Win32.Registry]::GetValue('HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots', 'KitsRoot10', $null)
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10')
    ) | Where-Object { $_ } | Select-Object -Unique
    $sdkLocation = $null
    foreach ($sdkRoot in $sdkRoots) {
        $includeRoot = Join-Path $sdkRoot 'Include'
        if (-not (Test-Path $includeRoot -PathType Container)) { continue }
        foreach ($includeVersion in (Get-ChildItem $includeRoot -Directory)) {
            $headerPath = Join-Path $includeVersion.FullName 'um\Windows.h'
            $libraryPath = Join-Path $sdkRoot ('Lib\' + $includeVersion.Name + '\um\x64\user32.lib')
            if ((Test-Path $headerPath -PathType Leaf) -and (Test-Path $libraryPath -PathType Leaf)) {
                $sdkLocation = $includeVersion.FullName; break
            }
        }
        if ($sdkLocation) { break }
    }
    Add-Check 'Windows SDK x64 (presence)' ([bool]$sdkLocation) $(if ($sdkLocation) { $sdkLocation } else { 'Add Windows SDK headers and x64 libraries.' }) ([bool]$RequireNative)
    $cmakeTool = Get-Command cmake -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $cmakePath = if ($cmakeTool) { $cmakeTool.Source } elseif ($nativeIde) {
        Join-Path ($nativeIde | Select-Object -First 1) 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    } else { $null }
    Add-Check 'CMake' ([bool]$cmakePath -and (Test-Path $cmakePath -PathType Leaf)) $(if ($cmakePath) { $cmakePath } else { 'Add C++ CMake tools for Windows.' }) ([bool]$RequireNative)

    $pythonTool = Get-Command py -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    # Presence only: invoking newer Python install managers can acquire a missing interpreter.
    # Native preparation explicitly requires an already installed Python 3.11+ in the guide.
    Add-Check 'Python launcher (presence)' ($null -ne $pythonTool) $(if ($pythonTool) { $pythonTool.Source } else { 'Install Python 3.11+ with its py launcher for native-helper checks.' }) ([bool]$RequireNative)

    if ($Json) { [pscustomobject]@{ Ready = ($failures.Count -eq 0); Checks = $checks.ToArray() } | ConvertTo-Json -Depth 4 }
    else {
        $checks | Format-Table -AutoSize -Wrap
        if ($failures.Count -gt 0) { Write-Host ('Missing required checks: ' + ($failures -join ', ')) }
        else { Write-Host 'Managed development prerequisites are ready. Native/manual acceptance is a separate step.' }
    }
} finally {
    Pop-Location
    [Environment]::SetEnvironmentVariable('DOTNET_CLI_TELEMETRY_OPTOUT', $previousDotnetTelemetry, 'Process')
}
if ($failures.Count -gt 0) { exit 1 }
exit 0
