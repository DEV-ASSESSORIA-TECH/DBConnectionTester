[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $MsiPath,
    [Parameter(Mandatory)][string] $PayloadDirectory
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallerTestHelpers.ps1')
$msi = [IO.Path]::GetFullPath($MsiPath)
$payload = [IO.Path]::GetFullPath($PayloadDirectory)
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)
try {
    $properties = Get-MsiPropertyMap $database
    Assert-InstallerCondition ($properties.ALLUSERS -eq '2' -and $properties.MSIINSTALLPERUSER -eq '1') 'MSI must default to per-user with both contexts supported.'
    Assert-InstallerCondition ($properties.ProductLanguage -eq '1046') 'Installer must use Portuguese (Brazil).'
    Assert-InstallerCondition ($properties.REBOOT -eq 'ReallySuppress') 'Installer must not force reboot.'
    Assert-InstallerCondition ($properties.MSIRESTARTMANAGERCONTROL -eq 'DisableShutdown') 'Installer must not shut applications down.'
    Assert-InstallerCondition ($properties.MSIDISABLERMRESTART -eq '1') 'Installer must not restart applications.'
    $files = @(Read-MsiTable $database 'SELECT `File`, `FileName`, `FileSize` FROM `File`' @('Id', 'Name', 'Size'))
    Assert-InstallerCondition ($files.Count -eq 4) 'Only the GUI, CLI, license and instructions belong in this MSI.'
    foreach ($entry in @{ DesktopExe = 'DBConnectionTester.exe'; CliExe = 'dbct.exe' }.GetEnumerator()) {
        $row = @($files | Where-Object Id -eq $entry.Key)
        Assert-InstallerCondition ($row.Count -eq 1) "Missing executable: $($entry.Value)"
        Assert-InstallerCondition ([long]$row[0].Size -eq (Get-Item -LiteralPath (Join-Path $payload $entry.Value)).Length) "Executable size differs from validated payload: $($entry.Value)"
    }
    $registry = @(Read-MsiTable $database 'SELECT `Key` FROM `Registry`' @('Key'))
    foreach ($entry in $registry) {
        Assert-InstallerCondition ($entry.Key -eq 'Software\DBConnectionTester.Installer') 'MSI must never own application data or preference registry keys.'
    }
    $environment = @(Read-MsiTable $database 'SELECT `Name`, `Value`, `Component_` FROM `Environment`' @('Name', 'Value', 'Component'))
    Assert-InstallerCondition ($environment.Count -eq 2) 'Expected separate user and machine PATH components.'
    Assert-InstallerCondition (($environment | Where-Object Component -eq 'UserPath').Name -notmatch '\*') 'User PATH must remain in the user context.'
    Assert-InstallerCondition (($environment | Where-Object Component -eq 'MachinePath').Name -match '\*') 'Machine PATH must use the system environment.'
    foreach ($entry in $environment) {
        Assert-InstallerCondition ($entry.Value -match '\[~\]' -and $entry.Value -match '\[DBCT_PATH_DIRECTORY\]') 'PATH must be appended, not replaced.'
    }
    $remove = @(Read-MsiTable $database 'SELECT `FileName`, `DirProperty` FROM `RemoveFile`' @('Name', 'Directory'))
    foreach ($entry in $remove) {
        Assert-InstallerCondition (-not $entry.Name -and $entry.Directory -in @('UserProductMenu', 'MachineProductMenu')) 'No recursive or wildcard deletion is allowed.'
    }
    $shortcuts = @(Read-MsiTable $database 'SELECT `Target` FROM `Shortcut`' @('Target'))
    Assert-InstallerCondition ($shortcuts.Count -eq 4) 'Expected two conditional GUI/documentation shortcut pairs, one per context.'
    $licenseControl = @(Read-MsiTable $database 'SELECT `Text` FROM `Control` WHERE `Dialog_`=''DBCT_License'' AND `Control`=''License''' @('Text'))
    Assert-InstallerCondition ($licenseControl.Count -eq 1 -and $licenseControl[0].Text -match '\\u\d+\?') 'Portuguese license must retain Unicode text.'
    Assert-InstallerCondition ($licenseControl[0].Text -notmatch '\\u(?!-?\d+\?)') 'Malformed Unicode escape in the installer license.'
    $actions = @(Read-MsiTable $database 'SELECT `Action`, `Type` FROM `CustomAction`' @('Name', 'Type'))
    Assert-InstallerCondition (@($actions | Where-Object { ([int]$_.Type -band 63) -in @(2, 18, 34, 50) }).Count -eq 0) 'MSI must not launch an application executable.'
    $media = @(Read-MsiTable $database 'SELECT `Cabinet` FROM `Media`' @('Cabinet'))
    Assert-InstallerCondition ($media.Count -eq 1 -and $media[0].Cabinet.StartsWith('#')) 'MSI must embed its cabinet.'

    if (-not ('DBCT.InstallerInspection' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
namespace DBCT {
    public static class InstallerInspection {
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiOpenDatabase(string path, IntPtr mode, out uint db);
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiDatabaseOpenView(uint db, string query, out uint view);
        [DllImport("msi.dll")] static extern uint MsiViewExecute(uint view, uint record);
        [DllImport("msi.dll")] static extern uint MsiViewFetch(uint view, out uint record);
        [DllImport("msi.dll")] static extern uint MsiRecordReadStream(uint record, uint field, byte[] buffer, ref uint count);
        [DllImport("msi.dll")] static extern uint MsiCloseHandle(uint handle);
        public static void ExtractCabinet(string msi, string stream, string path) {
            uint db=0, view=0, record=0;
            try {
                Check(MsiOpenDatabase(msi, IntPtr.Zero, out db));
                Check(MsiDatabaseOpenView(db, "SELECT `Data` FROM `_Streams` WHERE `Name`='" + stream.Replace("'", "''") + "'", out view));
                Check(MsiViewExecute(view, 0)); Check(MsiViewFetch(view, out record));
                using (var output=File.Create(path)) {
                    var buffer=new byte[65536];
                    while (true) {
                        uint count=(uint)buffer.Length;
                        Check(MsiRecordReadStream(record, 1, buffer, ref count));
                        if (count==0) break;
                        output.Write(buffer, 0, (int)count);
                    }
                }
            } finally { if(record!=0) MsiCloseHandle(record); if(view!=0) MsiCloseHandle(view); if(db!=0) MsiCloseHandle(db); }
        }
        static void Check(uint code) { if(code!=0) throw new InvalidOperationException("MSI read error: " + code); }
    }
}
'@
    }
    $workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $temporary = Join-Path $workspace ('artifacts/msi-inspection-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temporary -Force | Out-Null
    try {
        $cabinet = Join-Path $temporary 'payload.cab'
        [DBCT.InstallerInspection]::ExtractCabinet($msi, $media[0].Cabinet.Substring(1), $cabinet)
        & expand.exe '-F:*' $cabinet $temporary | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot extract embedded cabinet for hash verification.' }
        foreach ($entry in @{ DesktopExe = 'DBConnectionTester.exe'; CliExe = 'dbct.exe' }.GetEnumerator()) {
            $expected = (Get-FileHash -LiteralPath (Join-Path $payload $entry.Value) -Algorithm SHA256).Hash
            $actual = (Get-FileHash -LiteralPath (Join-Path $temporary $entry.Key) -Algorithm SHA256).Hash
            Assert-InstallerCondition ($actual -eq $expected) "MSI binary differs from validated portable: $($entry.Value)"
        }
    }
    finally {
        $prefix = $workspace.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not ([IO.Path]::GetFullPath($temporary)).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe inspection cleanup path.' }
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
    Write-Host 'MSI structure and payload hashes: OK (product not installed).'
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
