[CmdletBinding()]
param([Parameter(Mandatory)][string] $PayloadDirectory, [switch] $DisposableEnvironment)

$ErrorActionPreference = 'Stop'
if (-not $DisposableEnvironment -or ($env:GITHUB_ACTIONS -ne 'true' -and $env:DBCT_INSTALLER_DISPOSABLE -ne '1')) {
    throw 'Refusing installation on a development machine. Run only in a disposable Windows VM, with -DisposableEnvironment and GITHUB_ACTIONS=true or DBCT_INSTALLER_DISPOSABLE=1.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'The disposable test orchestrator requires an administrator to create an isolated standard account.' }
if (Test-Path 'HKLM:\Software\DBConnectionTester.Installer') { throw 'A machine installation already exists; refuse to test over it.' }
if (Test-Path 'HKCU:\Software\DBConnectionTester.Installer') { throw 'A user installation already exists; refuse to test over it.' }

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $PSScriptRoot 'Test-InstallerAccountEnvironment.ps1')
$fixtureRoot = Join-Path $repositoryRoot ('artifacts/installer-lifecycle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$fixtures = Join-Path $fixtureRoot 'packages'
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
foreach ($fixture in @(@{Name='base'; Version='252.0.0'}, @{Name='upgrade'; Version='252.0.1'}, @{Name='rollback'; Version='252.0.2'})) {
    $output = Join-Path $fixtureRoot $fixture.Name
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') -PayloadDirectory $PayloadDirectory -OutputDirectory $output -Version $fixture.Version | Out-Host
    Copy-Item -LiteralPath (Join-Path $output 'DBConnectionTester.msi') -Destination (Join-Path $fixtures ($fixture.Name + '.msi'))
}
# Inject a transaction failure only into the disposable fixture, never the shipping MSI.
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase((Join-Path $fixtures 'rollback.msi'), 1)
try {
    foreach ($sql in @(
        'INSERT INTO `CustomAction` (`Action`,`Type`,`Target`) VALUES (''FixtureRollback'',19,''Intentional disposable rollback test'')',
        'INSERT INTO `InstallExecuteSequence` (`Action`,`Condition`,`Sequence`) VALUES (''FixtureRollback'',''1'',4001)'
    )) {
        $view = $db.OpenView($sql)
        try { [void]$view.Execute() } finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    }
    [void]$db.Commit()
    $summary = $db.SummaryInformation(1)
    try { $summary.Property(9) = [guid]::NewGuid().ToString('B'); [void]$summary.Persist() }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) }
    [void]$db.Commit()
}
finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

$account = 'dbct' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$password = ConvertTo-SecureString ('Dbct!9' + [guid]::NewGuid().ToString('N') + 'xA') -AsPlainText -Force
$credential = [Management.Automation.PSCredential]::new("$env:COMPUTERNAME\$account", $password)
$publicRoot = Join-Path $env:PUBLIC ('DBCTInstallerTests/' + [guid]::NewGuid().ToString('N'))
$previousDisposable = $env:DBCT_INSTALLER_DISPOSABLE
$env:DBCT_INSTALLER_DISPOSABLE = '1'
try {
    New-LocalUser -Name $account -Password $password -AccountNeverExpires -Description 'Disposable DBCT installer acceptance account' | Out-Null
    $group = Get-LocalGroup -SID 'S-1-5-32-545'
    Add-LocalGroupMember -Group $group.Name -Member $account -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $publicRoot -Force | Out-Null
    Copy-Item -LiteralPath $fixtures -Destination $publicRoot -Recurse
    foreach ($file in @('InstallerLifecycle.Worker.ps1', 'InstallerTestHelpers.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $publicRoot }
    Set-Content -LiteralPath (Join-Path $publicRoot '.disposable-installer-test') -Value 'Explicit disposable lifecycle test'
    & icacls.exe $publicRoot /grant "${account}:(OI)(CI)M" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot grant the standard test account access to its isolated workspace.' }

    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $worker = Join-Path $publicRoot 'InstallerLifecycle.Worker.ps1'
    $packageDirectory = Join-Path $publicRoot 'packages'
    $arguments = @('-NoProfile', '-NonInteractive', '-File', "`"$worker`"", '-Scope', 'user',
        '-FixtureDirectory', "`"$packageDirectory`"", '-TestWorkspace', "`"$publicRoot`"", '-RequireStandardUser', '-DisposableEnvironment')
    $process = Start-Process -FilePath $pwsh -ArgumentList $arguments -Credential $credential -LoadUserProfile `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $publicRoot 'standard-user.stdout.log') `
        -RedirectStandardError (Join-Path $publicRoot 'standard-user.stderr.log')
    try {
        if (-not $process.WaitForExit(600000)) { throw 'Standard-user installer tests timed out; discard this VM.' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $publicRoot 'user.success'))) {
            foreach ($stream in @('stdout', 'stderr')) {
                $streamLog = Join-Path $publicRoot "standard-user.$stream.log"
                if (Test-Path -LiteralPath $streamLog) { Write-Host "--- standard-user $stream ---"; Get-Content -LiteralPath $streamLog | Out-Host }
            }
            throw "Standard-user acceptance failed. Inspect $publicRoot."
        }
    }
    finally { $process.Dispose() }
    foreach ($scope in @('machine', 'coexistence')) {
        & $pwsh -NoProfile -NonInteractive -File $worker -Scope $scope -FixtureDirectory $packageDirectory -TestWorkspace $publicRoot -DisposableEnvironment
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $publicRoot "$scope.success"))) { throw "$scope acceptance failed. Inspect $publicRoot." }
    }
    Write-Host 'Installer lifecycle: standard user, machine, coexistence, repair, upgrade, downgrade, rollback, file-use and data preservation OK.'
}
finally {
    # Keep all logs for diagnostics. The enclosing runner/VM is disposable.
    if (Get-LocalUser -Name $account -ErrorAction SilentlyContinue) { Remove-LocalUser -Name $account }
    $env:DBCT_INSTALLER_DISPOSABLE = $previousDisposable
    Write-Host "Lifecycle logs: $publicRoot"
}
