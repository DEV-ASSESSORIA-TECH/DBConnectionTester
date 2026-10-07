[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutputRoot = "",
    [switch] $NoRestore
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

$project = Join-Path $repositoryRoot "DBConnectionTester.csproj"
$publishRoot = Join-Path $outputRootPath "publish"
$artifactRoot = Join-Path $outputRootPath "artifacts"
$frameworkDirectory = Join-Path $publishRoot "framework-dependent"
$selfContainedDirectory = Join-Path $publishRoot "self-contained"
$standaloneDirectory = Join-Path $publishRoot "standalone"

foreach ($directory in @($frameworkDirectory, $selfContainedDirectory, $standaloneDirectory, $artifactRoot)) {
    if (Test-Path -LiteralPath $directory) {
        Remove-Item -LiteralPath $directory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if (-not $NoRestore) {
    & dotnet restore $project -r $Runtime
    if ($LASTEXITCODE -ne 0) { throw "runtime restore failed." }
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

$frameworkZip = Join-Path $artifactRoot "DBConnectionTester-v$version-win-x64-framework-dependent.zip"
$selfContainedZip = Join-Path $artifactRoot "DBConnectionTester-v$version-win-x64-self-contained.zip"
$standaloneArtifact = Join-Path $artifactRoot "DBConnectionTester-v$version-win-x64-self-contained.exe"
Compress-Archive -Path (Join-Path $frameworkDirectory "*") -DestinationPath $frameworkZip -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $selfContainedDirectory "*") -DestinationPath $selfContainedZip -CompressionLevel Optimal
Copy-Item -LiteralPath (Join-Path $standaloneDirectory "DBConnectionTester.exe") -Destination $standaloneArtifact

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

$artifacts = @($frameworkZip, $selfContainedZip, $standaloneArtifact)
$checksumLines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    $fileName = [System.IO.Path]::GetFileName($artifact)
    "$hash  $fileName" | Set-Content -LiteralPath "$artifact.sha256" -Encoding ascii
    "$hash  $fileName"
}
$checksumLines | Set-Content -LiteralPath (Join-Path $artifactRoot "SHA256SUMS.txt") -Encoding ascii

[pscustomobject]@{
    Version = $version
    FrameworkDependentZipBytes = (Get-Item -LiteralPath $frameworkZip).Length
    SelfContainedZipBytes = (Get-Item -LiteralPath $selfContainedZip).Length
    StandaloneExeBytes = $standaloneSize
} | Format-List
