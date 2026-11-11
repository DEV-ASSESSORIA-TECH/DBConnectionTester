[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutputRoot = "",
    [switch] $NoRestore,
    [string] $WixAcceptEula = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $repositoryRoot
}
$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($outputRootPath -ne $repositoryRoot -and -not $outputRootPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must be the repository or one of its subdirectories."
}

$version = (Get-Content -Raw (Join-Path $repositoryRoot "VERSION")).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "VERSION must use semantic version format."
}

$project = Join-Path $repositoryRoot "src/DBConnectionTester.Desktop/DBConnectionTester.Desktop.csproj"
$cliProject = Join-Path $repositoryRoot "src/DBConnectionTester.Cli/DBConnectionTester.Cli.csproj"
$publishRoot = Join-Path $outputRootPath "publish"
$artifactRoot = Join-Path $outputRootPath "artifacts"
$frameworkDirectory = Join-Path $publishRoot "framework-dependent"
$selfContainedDirectory = Join-Path $publishRoot "self-contained"
$standaloneDirectory = Join-Path $publishRoot "standalone"
$cliDirectory = Join-Path $publishRoot "cli"
$completeDirectory = Join-Path $publishRoot "complete"
$installerDirectory = Join-Path $publishRoot "installer"

foreach ($directory in @($frameworkDirectory, $selfContainedDirectory, $standaloneDirectory, $cliDirectory, $completeDirectory, $installerDirectory, $artifactRoot)) {
    $resolvedDirectory = [System.IO.Path]::GetFullPath($directory)
    if (-not $resolvedDirectory.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a directory outside the repository: $resolvedDirectory"
    }
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (-not $NoRestore) {
    & dotnet restore $project -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "runtime restore failed." }
    & dotnet restore $cliProject -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "CLI runtime restore failed." }
}

$commonProperties = @(
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-p:Version=$version",
    "-p:ContinuousIntegrationBuild=true"
)

& dotnet publish $project -c $Configuration -r $Runtime --self-contained false --no-restore `
    -p:PublishSingleFile=false @commonProperties -o $frameworkDirectory
if ($LASTEXITCODE -ne 0) { throw "Framework-dependent publish failed." }

& dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-restore `
    -p:PublishSingleFile=false @commonProperties -o $selfContainedDirectory
if ($LASTEXITCODE -ne 0) { throw "Self-contained publish failed." }

& dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-restore `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true @commonProperties -o $standaloneDirectory
if ($LASTEXITCODE -ne 0) { throw "Standalone publish failed." }

& dotnet publish $cliProject -c $Configuration -r $Runtime --self-contained true --no-restore `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true @commonProperties -o $cliDirectory
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }

Copy-Item -LiteralPath (Join-Path $standaloneDirectory "DBConnectionTester.exe") -Destination $completeDirectory
Copy-Item -LiteralPath (Join-Path $cliDirectory "dbct.exe") -Destination $completeDirectory
$portableGuide = Join-Path $repositoryRoot "docs/PORTABLE.md"
Copy-Item -LiteralPath $portableGuide -Destination $completeDirectory
Copy-Item -LiteralPath $portableGuide -Destination $cliDirectory

$frameworkZip = Join-Path $artifactRoot "DBConnectionTester-v$version-win-x64-framework-dependent.zip"
$selfContainedZip = Join-Path $artifactRoot "DBConnectionTester-v$version-win-x64-self-contained.zip"
$standaloneArtifact = Join-Path $artifactRoot "DBConnectionTester-v$version-win-x64-self-contained.exe"
$completeZip = Join-Path $artifactRoot "DBConnectionTester-v$version-$Runtime-portable-complete.zip"
$cliZip = Join-Path $artifactRoot "DBConnectionTester-v$version-$Runtime-portable-cli.zip"
Compress-Archive -Path (Join-Path $frameworkDirectory "*") -DestinationPath $frameworkZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $selfContainedDirectory "*") -DestinationPath $selfContainedZip -CompressionLevel Optimal
Copy-Item -LiteralPath (Join-Path $standaloneDirectory "DBConnectionTester.exe") -Destination $standaloneArtifact
Compress-Archive -Path (Join-Path $completeDirectory "*") -DestinationPath $completeZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $cliDirectory "*") -DestinationPath $cliZip -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($zipPath in @($frameworkZip, $selfContainedZip)) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        if ($archive.Entries.Count -eq 0 -or -not ($archive.Entries | Where-Object FullName -eq "DBConnectionTester.exe")) {
            throw "Package $zipPath is empty or does not contain DBConnectionTester.exe."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$maximumStandaloneBytes = 120MB
$standaloneSize = (Get-Item -LiteralPath $standaloneArtifact).Length
if ($standaloneSize -gt $maximumStandaloneBytes) {
    throw "Standalone executable is $standaloneSize bytes; the limit is $maximumStandaloneBytes bytes."
}

& (Join-Path $PSScriptRoot "Build-Installer.ps1") -PayloadDirectory $completeDirectory `
    -OutputDirectory $installerDirectory -Version $version -WixAcceptEula $WixAcceptEula | Out-Host
$msi = Join-Path $installerDirectory "DBConnectionTester.msi"
& (Join-Path $PSScriptRoot "Test-InstallerPackage.ps1") -MsiPath $msi -PayloadDirectory $completeDirectory
& (Join-Path $PSScriptRoot "Test-InstallerActions.ps1") -MsiPath $msi -PayloadDirectory $completeDirectory
$msiArtifact = Join-Path $artifactRoot "DBConnectionTester-v$version-$Runtime.msi"
Copy-Item -LiteralPath $msi -Destination $msiArtifact

$artifacts = @($frameworkZip, $selfContainedZip, $standaloneArtifact, $completeZip, $cliZip, $msiArtifact)
$checksumLines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileName = [System.IO.Path]::GetFileName($artifact)
    "$hash  $fileName" | Set-Content -LiteralPath "$artifact.sha256" -Encoding ascii
    "$hash  $fileName"
}
$checksumLines | Set-Content -LiteralPath (Join-Path $artifactRoot "SHA256SUMS.txt") -Encoding ascii

& (Join-Path $PSScriptRoot "Test-PortablePackages.ps1") -CompleteZip $completeZip -CliZip $cliZip

[pscustomobject]@{
    Version = $version
    FrameworkDependentZipBytes = (Get-Item -LiteralPath $frameworkZip).Length
    SelfContainedZipBytes = (Get-Item -LiteralPath $selfContainedZip).Length
    StandaloneExeBytes = $standaloneSize
    PortableCompleteZipBytes = (Get-Item -LiteralPath $completeZip).Length
    PortableCliZipBytes = (Get-Item -LiteralPath $cliZip).Length
    MsiBytes = (Get-Item -LiteralPath $msiArtifact).Length
} | Format-List
