[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Filter,
    [switch]$NoBuild,
    [switch]$NoRestore,
    [string[]]$Logger = @(),
    [string]$ResultsDirectory
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$env:COTABBY_CLIPBOARD_TEST = '0'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests/Cotabby.App.Wpf.Tests/Cotabby.App.Wpf.Tests.csproj'
$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.AddRange([string[]]@('test', $project, '--configuration', $Configuration, '--blame-hang-timeout', '2m'))
if ($NoBuild) { $arguments.Add('--no-build') }
if ($NoRestore) { $arguments.Add('--no-restore') }
if ($Filter) { $arguments.Add('--filter'); $arguments.Add($Filter) }
foreach ($item in $Logger) { $arguments.Add('--logger'); $arguments.Add($item) }
if ($ResultsDirectory) { $arguments.Add('--results-directory'); $arguments.Add($ResultsDirectory) }

# Independent worktrees share the interactive Windows desktop. A process-local STA does
# not isolate focus, popup dismissal or UIA events from another testhost. Acquire outside
# VSTest so waiting for a different suite does not consume a test's own timeout.
$lease = [System.Threading.Mutex]::new($false, 'Local\Cotabby.WpfDesktopTests.v1')
$owned = $false
try {
    try { $owned = $lease.WaitOne([TimeSpan]::FromMinutes(30)) }
    catch [System.Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { throw 'Timed out waiting for the WPF desktop test lease.' }
    & dotnet @arguments
    $testExit = $LASTEXITCODE
}
finally {
    if ($owned) { $lease.ReleaseMutex() }
    $lease.Dispose()
}
exit $testExit
