[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('user', 'machine', 'coexistence')][string] $Scope,
    [Parameter(Mandatory)][string] $FixtureDirectory,
    [Parameter(Mandatory)][string] $TestWorkspace,
    [switch] $RequireStandardUser,
    [switch] $DisposableEnvironment
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallerTestHelpers.ps1')
if (-not $DisposableEnvironment -or -not (Test-Path -LiteralPath (Join-Path $TestWorkspace '.disposable-installer-test'))) {
    throw 'Lifecycle tests only run in an explicitly marked disposable Windows environment.'
}
# CreateProcessWithLogon can inherit the orchestrator's environment; use the loaded account's folders.
$env:USERPROFILE = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
$env:LOCALAPPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$env:APPDATA = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
$env:TEMP = Join-Path $env:LOCALAPPDATA 'Temp'
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($RequireStandardUser -and $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Per-user acceptance must run without administrator membership.'
}
$installer = New-Object -ComObject WindowsInstaller.Installer
$packages = @{}
foreach ($name in @('base', 'upgrade', 'rollback')) {
    $path = Join-Path $FixtureDirectory "$name.msi"
    $db = $installer.OpenDatabase($path, 0)
    try { $packages[$name] = @{ Path = $path; Properties = (Get-MsiPropertyMap $db) } }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db) }
}
if (-not ('DBCT.ProductContext' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
namespace DBCT {
    public static class ProductContext {
        delegate bool WindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiGetProductInfoEx(string product, string sid, uint context, string property, StringBuilder value, ref uint size);
        public static string Version(string product, bool machine) {
            var value=new StringBuilder(256); uint size=256;
            uint result=MsiGetProductInfoEx(product, null, machine?4u:2u, "VersionString", value, ref size);
            if(result==1605) return null;
            if(result!=0) throw new InvalidOperationException("Product query error: " + result);
            return value.ToString();
        }
        public static IntPtr Window(uint process) {
            IntPtr found=IntPtr.Zero;
            EnumWindows((window, parameter) => {
                uint id; GetWindowThreadProcessId(window, out id);
                var name=new StringBuilder(256); GetClassName(window, name, name.Capacity);
                if(id==process && name.ToString().StartsWith("WindowsForms10.Window")) { found=window; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }
        public static void Close(IntPtr window) { if(!PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("Could not close fixture GUI."); }
    }
}
'@
}
$counter = 0
$attempted = [Collections.Generic.List[object]]::new()
function Invoke-Msi {
    param([string] $Package, [string] $Context, [string[]] $Extra = @(), [switch] $Remove, [switch] $ExpectFailure)
    $script:counter++
    $log = Join-Path $TestWorkspace ("$Scope-$($script:counter).log")
    $info = [Diagnostics.ProcessStartInfo]::new('msiexec.exe')
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    foreach ($argument in @($(if ($Remove) { '/x' } else { '/i' }), $Package, '/qn', '/norestart', '/l*v', $log,
        'ALLUSERS=2', $(if ($Context -eq 'machine') { 'MSIINSTALLPERUSER=' } else { 'MSIINSTALLPERUSER=1' })) + $Extra) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit(120000)) { throw "MSI timed out. Inspect $log; this VM must be discarded." }
        if ($ExpectFailure) { Assert-InstallerCondition ($process.ExitCode -notin @(0, 3010)) "MSI should have refused this operation: $log" }
        else { Assert-InstallerCondition ($process.ExitCode -in @(0, 3010)) "MSI failed with $($process.ExitCode): $log" }
    }
    finally { $process.Dispose() }
}
function Get-InstallDirectory([string] $Context) {
    if ($Context -eq 'machine') { return Join-Path $env:ProgramFiles 'DBConnectionTester' }
    return Join-Path $env:LOCALAPPDATA 'Programs/DBConnectionTester'
}
function Get-PathValue([string] $Context) {
    return [Environment]::GetEnvironmentVariable('PATH', $(if ($Context -eq 'machine') { 'Machine' } else { 'User' }))
}
function Assert-PathCount([string] $Context, [int] $Count) {
    $folder = (Get-InstallDirectory $Context).TrimEnd('\', '/')
    $entries = @(([string](Get-PathValue $Context)).Split(';', [StringSplitOptions]::RemoveEmptyEntries) | Where-Object { $_.TrimEnd('\', '/') -ieq $folder })
    Assert-InstallerCondition ($entries.Count -eq $Count) "Incorrect $Context PATH entry count: $($entries.Count)"
}
function Get-Snapshot {
    $snapshot = [ordered]@{}
    foreach ($file in Get-ChildItem -LiteralPath $script:data -Recurse -File | Sort-Object FullName) { $snapshot[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
    return ($snapshot | ConvertTo-Json -Compress)
}
function Invoke-Cli([string] $Executable, [string[]] $Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false; $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); throw 'Installed CLI timed out.' }
        Assert-InstallerCondition ($process.ExitCode -eq 0) ('Installed CLI failed: ' + $stderr.GetAwaiter().GetResult())
        return $stdout.GetAwaiter().GetResult()
    }
    finally { $process.Dispose() }
}
function Install-Package([string] $Name, [string] $Context, [string[]] $Extra = @()) {
    $attempted.Add(@{ Name = $Name; Context = $Context })
    Invoke-Msi $packages[$Name].Path $Context $Extra
    Assert-InstallerCondition ([DBCT.ProductContext]::Version($packages[$Name].Properties.ProductCode, $Context -eq 'machine') -eq $packages[$Name].Properties.ProductVersion) "Product version missing in $Context context."
}

$data = Join-Path $TestWorkspace "data-$Scope"
New-Item -ItemType Directory -Path $data -Force | Out-Null
$registry = 'HKCU:\Software\DBConnectionTester'
if (Test-Path -LiteralPath $registry) { throw 'A functional preference key already exists; refuse to touch an existing user environment.' }
New-Item -Path $registry -Force | Out-Null
$sentinel = [guid]::NewGuid().ToString()
New-ItemProperty -Path $registry -Name 'InstallerTestSentinel' -Value $sentinel | Out-Null
$active = $null
$listener = $null
try {
    if ($Scope -eq 'coexistence') {
        Install-Package 'base' 'user' @('ADD_TO_PATH=1')
        Install-Package 'base' 'machine' @('ADD_TO_PATH=1')
        Assert-PathCount 'user' 1; Assert-PathCount 'machine' 1
        Invoke-Msi $packages.base.Path 'machine' -Remove
        Assert-PathCount 'machine' 0; Assert-PathCount 'user' 1
        Assert-InstallerCondition (Test-Path -LiteralPath (Join-Path (Get-InstallDirectory 'user') 'dbct.exe')) 'Machine uninstall removed the user installation.'
        Install-Package 'upgrade' 'machine'
        Assert-InstallerCondition ([DBCT.ProductContext]::Version($packages.base.Properties.ProductCode, $false) -eq $packages.base.Properties.ProductVersion) 'Machine upgrade changed the user installation.'
        Invoke-Msi $packages.base.Path 'user' -Remove
        Assert-PathCount 'user' 0; Assert-PathCount 'machine' 1
    }
    else {
        $context = $Scope
        $folder = Get-InstallDirectory $context
        $oldPath = Get-PathValue $context
        Install-Package 'base' $context @('ADD_TO_PATH=0')
        Assert-PathCount $context 0
        Assert-InstallerCondition ((Get-PathValue $context) -eq $oldPath) 'PATH opt-out changed existing entries.'
        $cli = Join-Path $folder 'dbct.exe'
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0); $listener.Start()
        $arguments = @('run', '--type', 'tcp', '--host', '127.0.0.1', '--port', $listener.LocalEndpoint.Port.ToString(),
            '--dns', 'false', '--ping', 'false', '--count', '1', '--data-dir', $data, '--format', 'json')
        $run = Invoke-Cli $cli $arguments | ConvertFrom-Json
        Assert-InstallerCondition ($run.data.completed -eq 1) 'Installed CLI did not complete the diagnostic.'
        $history = Invoke-Cli $cli @('history', 'list', '--data-dir', $data, '--format', 'json') | ConvertFrom-Json
        Assert-InstallerCondition ($history.data.totalItems -eq 1) 'Installed history is missing.'
        $report = Join-Path $TestWorkspace "$Scope-report.json"
        $null = Invoke-Cli $cli @('export', $run.data.runId, '--data-dir', $data, '--output', $report)
        if ($context -eq 'machine') {
            $guiInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $folder 'DBConnectionTester.exe'))
            $guiInfo.UseShellExecute = $false; $guiInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $guiInfo.ArgumentList.Add('--data-dir'); $guiInfo.ArgumentList.Add($data)
            $gui = [Diagnostics.Process]::Start($guiInfo)
            try {
                $deadline = [DateTime]::UtcNow.AddSeconds(30)
                do {
                    Start-Sleep -Milliseconds 100
                    $window = [DBCT.ProductContext]::Window([uint32]$gui.Id)
                } while ($window -eq [IntPtr]::Zero -and -not $gui.HasExited -and [DateTime]::UtcNow -lt $deadline)
                Assert-InstallerCondition ($window -ne [IntPtr]::Zero -and -not $gui.HasExited) 'Installed GUI did not initialize.'
                [DBCT.ProductContext]::Close($window)
                Assert-InstallerCondition ($gui.WaitForExit(30000)) 'Fixture GUI did not close normally.'
                Assert-InstallerCondition ($gui.ExitCode -eq 0) 'Installed GUI failed.'
            }
            finally { if (-not $gui.HasExited) { $gui.Kill($true); $gui.WaitForExit() }; $gui.Dispose() }
            # Restore only the preference created by this GUI smoke, before installer preservation checks.
            Remove-ItemProperty -LiteralPath $registry -Name 'StorageSelection' -ErrorAction SilentlyContinue
        }
        $snapshot = Get-Snapshot
        $reportHash = (Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash
        $extraFile = Join-Path $folder 'user-file.keep'
        Set-Content -LiteralPath $extraFile -Value 'user-owned data'
        Install-Package 'upgrade' $context
        Assert-PathCount $context 0
        Invoke-Msi $packages.upgrade.Path $context -Remove
        Assert-InstallerCondition ((Get-Snapshot) -eq $snapshot) 'Uninstall modified application data.'
        Assert-InstallerCondition (Test-Path -LiteralPath $extraFile) 'Uninstall removed a user-owned file.'

        # A pre-existing PATH token, including a trailing slash, belongs to its owner.
        $target = if ($context -eq 'machine') { 'Machine' } else { 'User' }
        $preExistingPath = ([string]$oldPath).TrimEnd(';') + ';' + $folder + '\'
        [Environment]::SetEnvironmentVariable('PATH', $preExistingPath, $target)
        try {
            Install-Package 'base' $context @('ADD_TO_PATH=1')
            Assert-PathCount $context 1
            Invoke-Msi $packages.base.Path $context -Remove
            Assert-InstallerCondition ((Get-PathValue $context) -eq $preExistingPath) 'MSI changed or removed a pre-existing PATH entry.'
        }
        finally { [Environment]::SetEnvironmentVariable('PATH', $oldPath, $target) }

        Install-Package 'base' $context @('ADD_TO_PATH=1')
        Assert-PathCount $context 1
        Remove-Item -LiteralPath $cli
        Invoke-Msi $packages.base.Path $context @('REINSTALL=ALL', 'REINSTALLMODE=amus')
        Assert-InstallerCondition (Test-Path -LiteralPath $cli) 'Repair did not restore CLI.'
        Assert-PathCount $context 1

        $info = [Diagnostics.ProcessStartInfo]::new($cli)
        $info.UseShellExecute = $false; $info.CreateNoWindow = $true
        $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
        foreach ($argument in $arguments + @('--continuous', 'true', '--interval', '1')) { $info.ArgumentList.Add($argument) }
        $active = [Diagnostics.Process]::Start($info)
        $activeOutput = $active.StandardOutput.ReadToEndAsync()
        $line = $active.StandardError.ReadLineAsync()
        if (-not $line.Wait(30000)) { throw 'Active-run fixture did not start.' }
        Assert-InstallerCondition ($line.GetAwaiter().GetResult() -match 'Ciclos:') 'Active-run fixture failed.'
        $activeError = $active.StandardError.ReadToEndAsync()
        Invoke-Msi $packages.upgrade.Path $context -ExpectFailure
        Invoke-Msi $packages.base.Path $context -Remove -ExpectFailure
        Assert-InstallerCondition (-not $active.HasExited) 'Installer terminated an active run.'
        $active.Kill($true); $active.WaitForExit(); $active.Dispose(); $active = $null
        $snapshot = Get-Snapshot

        Install-Package 'upgrade' $context
        Assert-PathCount $context 1
        Assert-InstallerCondition ((Get-Snapshot) -eq $snapshot) 'Upgrade modified application data.'
        Invoke-Msi $packages.base.Path $context -ExpectFailure
        Invoke-Msi $packages.rollback.Path $context -ExpectFailure
        Assert-InstallerCondition ([DBCT.ProductContext]::Version($packages.upgrade.Properties.ProductCode, $context -eq 'machine') -eq $packages.upgrade.Properties.ProductVersion) 'Rollback did not restore previous registration.'
        Assert-InstallerCondition (Test-Path -LiteralPath $cli) 'Rollback did not restore executables.'
        Assert-PathCount $context 1
        Assert-InstallerCondition ((Get-Snapshot) -eq $snapshot) 'Failed upgrade modified application data.'
        Invoke-Msi $packages.upgrade.Path $context -Remove
        Assert-PathCount $context 0
        Assert-InstallerCondition ((Get-PathValue $context) -eq $oldPath) 'Uninstall modified unrelated PATH entries.'
        Assert-InstallerCondition ((Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash -eq $reportHash) 'Installer modified an exported report.'
        Assert-InstallerCondition ((Get-Snapshot) -eq $snapshot) 'Final uninstall modified application data.'
    }
    Assert-InstallerCondition ((Get-ItemPropertyValue -Path $registry -Name 'InstallerTestSentinel') -eq $sentinel) 'Installer modified functional preferences.'
    Set-Content -LiteralPath (Join-Path $TestWorkspace "$Scope.success") -Value 'OK'
}
finally {
    if ($active) { if (-not $active.HasExited) { $active.Kill($true); $active.WaitForExit() }; $active.Dispose() }
    if ($listener) { $listener.Stop() }
    foreach ($entry in $attempted) {
        $package = $packages[$entry.Name]
        if ([DBCT.ProductContext]::Version($package.Properties.ProductCode, $entry.Context -eq 'machine')) {
            try { Invoke-Msi $package.Path $entry.Context -Remove } catch { Write-Warning "Disposable environment cleanup failed: $_" }
        }
    }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
    if ((Get-ItemPropertyValue -Path $registry -Name 'InstallerTestSentinel' -ErrorAction SilentlyContinue) -eq $sentinel) {
        Remove-ItemProperty -LiteralPath $registry -Name 'InstallerTestSentinel'
        $key = Get-Item -LiteralPath $registry
        if ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) { Remove-Item -LiteralPath $registry }
    }
}
