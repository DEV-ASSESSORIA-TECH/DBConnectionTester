[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $CompleteZip,
    [Parameter(Mandatory)][string] $CliZip
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = Join-Path $workspace ("artifacts/portable validation " + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

function Invoke-PortableCli {
    param([string] $Executable, [string[]] $Arguments)
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.WorkingDirectory = $temporaryRoot
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables["DOTNET_ROOT"] = (Join-Path $temporaryRoot "missing runtime")
    $info.EnvironmentVariables["DOTNET_ROOT_X64"] = (Join-Path $temporaryRoot "missing runtime")
    $info.EnvironmentVariables["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    $info.EnvironmentVariables["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = (Join-Path $temporaryRoot "native cache")
    $process = [System.Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) {
            $process.Kill()
            $process.WaitForExit()
            throw "Portable CLI timed out."
        }
        $text = $stdout.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($text)) {
            throw "Portable CLI failed (exit $($process.ExitCode)): $($stderr.GetAwaiter().GetResult())"
        }
        return $text
    }
    finally { $process.Dispose() }
}

try {
    foreach ($package in @(
        @{ Path = $CompleteZip; Name = "complete"; Files = @("DBConnectionTester.exe", "dbct.exe", "PORTABLE.md") },
        @{ Path = $CliZip; Name = "cli"; Files = @("dbct.exe", "PORTABLE.md") }
    )) {
        $destination = Join-Path $temporaryRoot $package.Name
        [System.IO.Compression.ZipFile]::ExtractToDirectory([System.IO.Path]::GetFullPath($package.Path), $destination)
        $actual = @(Get-ChildItem -LiteralPath $destination -Recurse -File | ForEach-Object { $_.Name })
        if (@(Compare-Object ($package.Files | Sort-Object) ($actual | Sort-Object)).Count -ne 0) {
            throw "Unexpected portable contents: $($package.Path)"
        }
        $cli = Join-Path $destination "dbct.exe"
        # Run from another current directory with runtime lookup redirected to an absent directory.
        $null = Invoke-PortableCli -Executable $cli -Arguments @("--version")
        if (Test-Path -LiteralPath (Join-Path $destination "Data")) { throw "Version command created portable data." }
        $dataDirectory = "data " + $package.Name
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
        $listener.Start()
        try {
            $port = $listener.LocalEndpoint.Port.ToString()
            $run = Invoke-PortableCli -Executable $cli -Arguments @(
                "run", "--type", "tcp", "--host", "127.0.0.1", "--port", $port,
                "--dns", "false", "--ping", "false", "--count", "1", "--timeout", "2",
                "--data-dir", $dataDirectory, "--format", "json"
            ) | ConvertFrom-Json
            if ($run.formatVersion -ne 1 -or $run.data.completed -ne 1 -or $run.data.hasDiagnosticFailures) {
                throw "Portable diagnostic did not complete successfully."
            }
        }
        finally { $listener.Stop() }
        $history = Invoke-PortableCli -Executable $cli -Arguments @(
            "history", "list", "--data-dir", $dataDirectory, "--format", "json"
        ) | ConvertFrom-Json
        if ($history.data.totalItems -ne 1) { throw "Portable history is missing the run." }
        $report = Join-Path $temporaryRoot ($package.Name + " report.json")
        $null = Invoke-PortableCli -Executable $cli -Arguments @(
            "export", $run.data.runId, "--data-dir", $dataDirectory, "--output", $report
        )
        $exported = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($exported.formatVersion -ne 1 -or $exported.cycles.Count -ne 1) { throw "Invalid portable export." }
    }
}
finally {
    $resolved = [System.IO.Path]::GetFullPath($temporaryRoot)
    $prefix = $workspace.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove validation directory outside workspace."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
