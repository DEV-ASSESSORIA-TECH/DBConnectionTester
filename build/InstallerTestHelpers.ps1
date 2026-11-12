# Read-only helpers shared by package inspection and disposable-environment lifecycle tests.
function Read-MsiTable {
    param([Parameter(Mandatory)] $Database, [Parameter(Mandatory)][string] $Sql,
          [Parameter(Mandatory)][string[]] $Columns)
    $view = $Database.OpenView($Sql)
    try {
        [void]$view.Execute()
        while ($record = $view.Fetch()) {
            try {
                $row = [ordered]@{}
                for ($index = 0; $index -lt $Columns.Count; $index++) { $row[$Columns[$index]] = $record.StringData($index + 1) }
                [pscustomobject]$row
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    }
    finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}

function Assert-InstallerCondition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Get-MsiPropertyMap {
    param($Database)
    $values = @{}
    foreach ($row in Read-MsiTable $Database 'SELECT `Property`, `Value` FROM `Property`' @('Name', 'Value')) {
        $values[$row.Name] = $row.Value
    }
    return $values
}

# Rebuild the current account's environment without using shell folder caches,
# which may have expanded the launching administrator's inherited USERPROFILE.
function Initialize-InstallerAccountEnvironment {
    param([Parameter(Mandatory)][string] $BootstrapDirectory)
    $env:TEMP = $BootstrapDirectory
    $env:TMP = $BootstrapDirectory
    if (-not ('DBCT.AccountEnvironment' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Security.Principal;
namespace DBCT {
    public static class AccountEnvironment {
        [DllImport("userenv.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool GetUserProfileDirectory(IntPtr token, StringBuilder path, ref uint size);
        public static string ProfileDirectory() {
            using (var identity=WindowsIdentity.GetCurrent()) {
                var path=new StringBuilder(32768); uint size=(uint)path.Capacity;
                if(!GetUserProfileDirectory(identity.Token, path, ref size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return path.ToString();
            }
        }
    }
}
'@
    }
    $env:USERPROFILE = [DBCT.AccountEnvironment]::ProfileDirectory()
    $shellFolders = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders')
    try {
        $rawLocal = if ($shellFolders) { $shellFolders.GetValue('Local AppData', '%USERPROFILE%\AppData\Local', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { '%USERPROFILE%\AppData\Local' }
        $rawRoaming = if ($shellFolders) { $shellFolders.GetValue('AppData', '%USERPROFILE%\AppData\Roaming', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { '%USERPROFILE%\AppData\Roaming' }
        $env:LOCALAPPDATA = [Environment]::ExpandEnvironmentVariables($rawLocal)
        $env:APPDATA = [Environment]::ExpandEnvironmentVariables($rawRoaming)
    }
    finally { if ($shellFolders) { $shellFolders.Dispose() } }
    $env:TEMP = Join-Path $env:LOCALAPPDATA 'Temp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
}
