[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Version,
    [string] $Runtime = "win-x64"
)

$packages = @(
    "DBConnectionTester-v$Version-$Runtime-framework-dependent.zip",
    "DBConnectionTester-v$Version-$Runtime-self-contained.zip",
    "DBConnectionTester-v$Version-$Runtime-self-contained.exe",
    "DBConnectionTester-v$Version-$Runtime-portable-complete.zip",
    "DBConnectionTester-v$Version-$Runtime-portable-cli.zip"
)
foreach ($package in $packages) {
    $package
    "$package.sha256"
}
"SHA256SUMS.txt"
