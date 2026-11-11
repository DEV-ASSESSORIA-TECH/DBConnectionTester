[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PayloadDirectory,
    [string] $OutputDirectory = "",
    [string] $Version = "",
    [string] $WixAcceptEula = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $repositoryRoot "VERSION") -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must contain three numeric fields." }
$parts = $Version.Split('.')
if ([int]$parts[0] -gt 255 -or [int]$parts[1] -gt 255 -or [int]$parts[2] -gt 65535) {
    throw "MSI version must fit 255.255.65535."
}

# Only forward a decision supplied by the owner. Never call 'wix eula accept'.
if (-not $WixAcceptEula) { $WixAcceptEula = $env:DBCT_WIX_ACCEPT_EULA }
if (-not $WixAcceptEula) {
    $environmentFile = Join-Path $repositoryRoot ".env"
    if (Test-Path -LiteralPath $environmentFile) {
        foreach ($line in Get-Content -LiteralPath $environmentFile) {
            if ($line -match '^\s*DBCT_WIX_ACCEPT_EULA\s*=\s*["'']?(wix7)["'']?\s*$') {
                $WixAcceptEula = $Matches[1]
            }
        }
    }
}
$acceptanceFile = Join-Path $env:USERPROFILE ".wix/wix7-osmf-eula.txt"
if (-not $WixAcceptEula -and -not (Test-Path -LiteralPath $acceptanceFile)) {
    throw "MSI build requires the owner's WiX OSMF acceptance. Review https://docs.firegiant.com/wix/osmf/ and supply DBCT_WIX_ACCEPT_EULA=wix7 via the environment or a local, untracked .env. No terms were accepted automatically."
}
if ($WixAcceptEula -and $WixAcceptEula -ne "wix7") { throw "Unsupported WiX acceptance ID supplied by the owner." }

$payload = [IO.Path]::GetFullPath($PayloadDirectory)
foreach ($name in @("DBConnectionTester.exe", "dbct.exe")) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $name) -PathType Leaf)) { throw "Validated payload is missing $name." }
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot "artifacts/msi-build" }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$prefix = $repositoryRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Installer output must be inside the repository." }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$projectDirectory = Join-Path $repositoryRoot "installer/DBConnectionTester.Installer"
$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) { throw "Install Visual Studio Build Tools with the x64 C++ tools and Windows SDK." }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw "The x64 C++ tools and Windows SDK are required for the native installer actions." }
$msbuild = Join-Path $visualStudio "MSBuild/Current/Bin/MSBuild.exe"
$nativeBin = Join-Path $output "native/bin/"
$nativeObj = Join-Path $output "native/obj/"
& $msbuild (Join-Path $projectDirectory "NativeActions/NativeActions.vcxproj") /nologo /m:1 /v:minimal `
    /p:Configuration=Release /p:Platform=x64 "/p:OutDir=$nativeBin" "/p:IntDir=$nativeObj"
if ($LASTEXITCODE -ne 0) { throw "Native installer actions build failed." }
$actions = Join-Path $nativeBin "DBConnectionTester.Installer.Actions.dll"

$rtf = [Text.StringBuilder]::new('{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 ')
foreach ($character in (Get-Content -LiteralPath (Join-Path $repositoryRoot "LICENSE.pt-BR") -Raw).ToCharArray()) {
    $code = [int]$character
    if ($code -eq 13) { continue }
    if ($code -eq 10) { [void]$rtf.Append('\par '); continue }
    if ($character -in @('\', '{', '}')) { [void]$rtf.Append('\') }
    if ($code -gt 127) {
        if ($code -gt 32767) { $code -= 65536 }
        [void]$rtf.Append("\u$code?")
    } else { [void]$rtf.Append($character) }
}
[void]$rtf.Append('}')
$licenseRtf = Join-Path $output "License.rtf"
[IO.File]::WriteAllText($licenseRtf, $rtf.ToString(), [Text.Encoding]::ASCII)

# Stable per-version identity; the MSI package code still changes for a rebuilt package.
$hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("DBConnectionTester.MSI.Product/$Version"))
$identity = [byte[]]$hash[0..15]
$identity[7] = ($identity[7] -band 15) -bor 128
$identity[8] = ($identity[8] -band 63) -bor 128
$productCode = ([guid]::new($identity)).ToString("B").ToUpperInvariant()
$properties = @(
    "-p:Version=$Version", "-p:PayloadDirectory=$payload", "-p:ActionsBinary=$actions",
    "-p:LicenseRtf=$licenseRtf", "-p:InstallerProductCode=$productCode", "-p:OutputPath=$output/"
)
if ($WixAcceptEula) { $properties += "-p:AcceptEula=$WixAcceptEula" }
& dotnet build (Join-Path $projectDirectory "DBConnectionTester.Installer.wixproj") `
    -c Release --disable-build-servers -m:1 @properties
if ($LASTEXITCODE -ne 0) { throw "MSI build or ICE validation failed." }
$msi = Join-Path $output "DBConnectionTester.msi"
if (-not (Test-Path -LiteralPath $msi)) { throw "The MSI output was not produced." }
[pscustomobject]@{ Path = $msi; Version = $Version; ProductCode = $productCode; Length = (Get-Item -LiteralPath $msi).Length }
