[CmdletBinding()]
param([Parameter(Mandatory)][string] $MsiPath, [Parameter(Mandatory)][string] $PayloadDirectory,
      [string] $ActionsBinary = '')

# Opens an isolated MSI session and invokes only our read-only immediate actions.
# Never executes INSTALL, InstallFiles, registry writes, or product registration.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'InstallerTestHelpers.ps1')
if (-not ('DBCT.InstallerSession' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
namespace DBCT {
    public sealed class InstallerSession : IDisposable {
        uint session;
        IntPtr library;
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiOpenPackageEx(string path, uint options, out uint handle);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint NativeAction(uint session);
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiSetProperty(uint handle, string name, string value);
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiGetProperty(uint handle, string name, StringBuilder value, ref uint size);
        [DllImport("msi.dll")] static extern uint MsiCloseHandle(uint handle);
        [DllImport("msi.dll")] static extern uint MsiSetInternalUI(uint level, IntPtr window);
        [DllImport("msi.dll", CharSet=CharSet.Unicode)] static extern uint MsiEnableLog(uint flags, string path, uint mode);
        public InstallerSession(string path, string actions) {
            MsiSetInternalUI(2, IntPtr.Zero);
            Check(MsiEnableLog(0x3fff, Path.Combine(Path.GetDirectoryName(path), "action-session.log"), 2));
            Check(MsiOpenPackageEx(path, 1, out session));
            library=LoadLibraryEx(actions, IntPtr.Zero, 0x1100);
            if(library==IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        public void Set(string name, string value) { Check(MsiSetProperty(session, name, value)); }
        public string Get(string name) { var text=new StringBuilder(32768); uint size=32768; Check(MsiGetProperty(session, name, text, ref size)); return text.ToString(); }
        public uint Action(string name) {
            if(name!="PrepareSettings" && name!="FilterRelatedProducts" && name!="CheckFilesInUse") throw new ArgumentException("Not a read-only test action.");
            var address=GetProcAddress(library, name);
            if(address==IntPtr.Zero) throw new InvalidOperationException("Missing native action.");
            return Marshal.GetDelegateForFunctionPointer<NativeAction>(address)(session);
        }
        static void Check(uint result) { if(result!=0) throw new InvalidOperationException("MSI session error: " + result); }
        public void Dispose() { if(library!=IntPtr.Zero) { FreeLibrary(library); library=IntPtr.Zero; } if(session!=0) { MsiCloseHandle(session); session=0; } }
    }
}
'@
}
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporary = Join-Path $workspace ('artifacts/msi-action-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
if (-not $ActionsBinary) { $ActionsBinary = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($MsiPath))) 'native/bin/DBConnectionTester.Installer.Actions.dll' }
$session = [DBCT.InstallerSession]::new([IO.Path]::GetFullPath($MsiPath), [IO.Path]::GetFullPath($ActionsBinary))
$process = $null
$listener = $null
try {
    $session.Set('ALLUSERS', '2')
    $session.Set('MSIINSTALLPERUSER', '1')
    $session.Set('ADD_TO_PATH', '0')
    Assert-InstallerCondition ($session.Action('PrepareSettings') -eq 0) 'Prepare per-user settings failed.'
    Assert-InstallerCondition ($session.Get('INSTALLFOLDER') -eq (Join-Path $env:LOCALAPPDATA 'Programs/DBConnectionTester/')) 'Incorrect per-user location.'
    Assert-InstallerCondition ($session.Get('ADD_TO_PATH') -eq '0') 'Explicit PATH opt-out was lost.'
    $session.Set('MSIINSTALLPERUSER', '')
    $session.Set('ADD_TO_PATH', '1')
    Assert-InstallerCondition ($session.Action('PrepareSettings') -eq 0) 'Prepare machine settings failed.'
    Assert-InstallerCondition ($session.Get('INSTALLFOLDER') -eq (Join-Path $env:ProgramFiles 'DBConnectionTester/')) 'Incorrect machine location.'
    $session.Set('WIX_UPGRADE_DETECTED', '{00000000-0000-0000-0000-000000000001}')
    Assert-InstallerCondition ($session.Action('FilterRelatedProducts') -eq 0) 'Context filtering failed.'
    Assert-InstallerCondition ($session.Get('WIX_UPGRADE_DETECTED') -eq '') 'Unknown product must not be upgraded.'

    $folder = $temporary.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $session.Set('INSTALLFOLDER', $folder)
    Assert-InstallerCondition ($session.Action('CheckFilesInUse') -eq 0) 'An unused folder must not block maintenance.'
    $cli = Join-Path $temporary 'dbct.exe'
    Copy-Item -LiteralPath (Join-Path ([IO.Path]::GetFullPath($PayloadDirectory)) 'dbct.exe') -Destination $cli
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $info = [Diagnostics.ProcessStartInfo]::new($cli)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in @('run', '--type', 'tcp', '--host', '127.0.0.1', '--port', $listener.LocalEndpoint.Port.ToString(),
        '--dns', 'false', '--ping', 'false', '--continuous', 'true', '--interval', '1', '--data-dir', (Join-Path $temporary 'data'))) {
        $info.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $line = $process.StandardError.ReadLineAsync()
    if (-not $line.Wait(30000)) { throw 'Fixture CLI did not begin its test.' }
    Assert-InstallerCondition ($line.GetAwaiter().GetResult() -match 'Ciclos:') 'Fixture CLI did not complete its first cycle.'
    $stderr = $process.StandardError.ReadToEndAsync()
    Assert-InstallerCondition ($session.Action('CheckFilesInUse') -eq 1603) 'Maintenance must refuse an active installed executable.'
    Assert-InstallerCondition (-not $process.HasExited) 'The installer must never shut down the application.'
    # A portable process in another directory must not block this installation.
    $session.Set('INSTALLFOLDER', (Join-Path $temporary 'other/') )
    Assert-InstallerCondition ($session.Action('CheckFilesInUse') -eq 0) 'Another directory must not be treated as this installation.'
    Write-Host 'Native actions: contexts, explicit PATH, scoped discovery and file-use guards OK (product not installed).'
}
finally {
    $session.Dispose()
    if ($process) {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    if ($listener) { $listener.Stop() }
    $prefix = $workspace.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not ([IO.Path]::GetFullPath($temporary)).StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe action-test cleanup path.' }
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
