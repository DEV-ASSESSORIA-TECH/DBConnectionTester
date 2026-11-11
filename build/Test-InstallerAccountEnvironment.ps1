[CmdletBinding()]
param([switch] $Child)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallerTestHelpers.ps1')
if ($Child) {
    Initialize-InstallerAccountEnvironment -BootstrapDirectory ([IO.Path]::GetTempPath())
    foreach ($name in @('USERPROFILE', 'LOCALAPPDATA', 'APPDATA')) {
        $actual = [Environment]::GetEnvironmentVariable($name)
        $expected = [Environment]::GetEnvironmentVariable("DBCT_EXPECTED_$name")
        Assert-InstallerCondition ($actual -eq $expected) "Inherited environment corrupted $name."
    }
    Write-Host 'Fresh-process account environment with incorrect inherited folders: OK.'
    exit 0
}

$info = [Diagnostics.ProcessStartInfo]::new((Get-Command pwsh -ErrorAction Stop).Source)
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
foreach ($argument in @('-NoProfile', '-NonInteractive', '-File', $PSCommandPath, '-Child')) { $info.ArgumentList.Add($argument) }
foreach ($name in @('USERPROFILE', 'LOCALAPPDATA', 'APPDATA')) {
    $info.Environment["DBCT_EXPECTED_$name"] = [Environment]::GetEnvironmentVariable($name)
    $info.Environment[$name] = 'C:\DBCT-invalid-inherited-profile'
}
$process = [Diagnostics.Process]::Start($info)
try {
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Account environment test timed out.' }
    Write-Host $stdout.GetAwaiter().GetResult()
    Assert-InstallerCondition ($process.ExitCode -eq 0) $stderr.GetAwaiter().GetResult()
}
finally { $process.Dispose() }
