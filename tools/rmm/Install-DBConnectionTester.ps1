#Requires -Version 5.1
<#
.SYNOPSIS
Instala ou atualiza o DB Connection Tester (GUI + CLI) para todos os usuarios, sem perguntas.

.DESCRIPTION
Para ferramentas de RMM (Tactical RMM e similares), que executam como SYSTEM e sem
terminal interativo. Baixa o MSI da release do GitHub (ou usa -MsiPath), confere o
SHA256 publicado junto, instala por maquina, com a CLI no PATH, e nao configura
nenhum monitoramento: isso e feito depois com "dbct run ... --autostart".

Atualizacao: o MSI nao encerra processos em uso. Antes de instalar, o script para as
execucoes automaticas que usam o dbct.exe desta instalacao (dbct run stop, registradas
como Stopped) e, ao final, retoma cada uma (dbct autostart start), inclusive se a
instalacao falhar. Uma GUI ou CLI aberta por um usuario ainda impede a atualizacao: o
MSI falha e o log indica o arquivo em uso.

Codigos de saida: 0 sucesso (inclusive com reinicio pendente do Windows Installer),
1 falha. O resumo e escrito na saida padrao.
Manter este arquivo em ASCII: o Windows PowerShell 5.1 le arquivos sem BOM como ANSI.

.PARAMETER Version
Versao da release a baixar, por exemplo 3.1.1. Padrao: latest (ultima release).

.PARAMETER MsiPath
MSI ja copiado para a maquina (sem acesso ao GitHub). Quando informado, nada e baixado.

.PARAMETER AddToPath
1 (padrao) adiciona a CLI ao PATH da maquina; 0 nao altera o PATH.

.PARAMETER LogPath
Log detalhado do Windows Installer.

.EXAMPLE
.\Install-DBConnectionTester.ps1

.EXAMPLE
.\Install-DBConnectionTester.ps1 -Version 3.1.1

.EXAMPLE
.\Install-DBConnectionTester.ps1 -MsiPath C:\Instaladores\DBConnectionTester-v3.1.1-win-x64.msi
#>
[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [string]$MsiPath = '',
    [ValidateSet(0, 1)][int]$AddToPath = 1,
    [string]$LogPath = (Join-Path $env:windir 'Temp\DBConnectionTester-install.log'),
    [ValidateRange(5, 600)][int]$StopTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# PowerShell 32 bits (alguns agentes de RMM) enxerga "Program Files (x86)" e o
# registro redirecionado: reexecuta no PowerShell 64 bits.
if ([Environment]::Is64BitOperatingSystem -and -not [Environment]::Is64BitProcess) {
    $ps64 = Join-Path $env:windir 'sysnative\WindowsPowerShell\v1.0\powershell.exe'
    & $ps64 -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -Version $Version -MsiPath $MsiPath `
        -AddToPath $AddToPath -LogPath $LogPath -StopTimeoutSeconds $StopTimeoutSeconds
    exit $LASTEXITCODE
}

$Repo = 'DEV-ASSESSORIA-TECH/DBConnectionTester'
$TaskPath = '\DB Connection Tester\'
$Dbct = Join-Path $env:ProgramFiles 'DBConnectionTester\dbct.exe'
$DownloadDir = Join-Path $env:windir 'Temp\DBConnectionTester-download'

# Write-Host: mensagens fora do retorno das funcoes (o RMM captura a saida do host).
function Write-Step([string]$Text) { Write-Host "[DBCT] $Text" }

function Test-Administrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Pastas dos bancos com execucao automatica desta instalacao, lidas das tarefas
# "dbct autostart run --data-dir <pasta>" registradas pelo proprio DBCT.
function Get-AutostartDirectories {
    $directories = @()
    foreach ($task in @(Get-ScheduledTask -TaskPath $TaskPath -ErrorAction SilentlyContinue)) {
        foreach ($action in @($task.Actions)) {
            if ([string]$action.Execute -ine $Dbct) { continue }
            if ([string]$action.Arguments -match '--data-dir\s+(?:"([^"]+)"|(\S+))') {
                $directory = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
                $directories += $directory.TrimEnd('\')
            }
        }
    }
    return @($directories | Select-Object -Unique)
}

function Invoke-Dbct([string[]]$Arguments) {
    # Windows PowerShell 5.1 trata linhas do stderr redirecionado como erro fatal
    # quando ErrorActionPreference e Stop; o dbct escreve avisos no stderr.
    $ErrorActionPreference = 'Continue'
    $output = & $Dbct @Arguments 2>&1 | Out-String
    return @{ ExitCode = $LASTEXITCODE; Output = $output.Trim() }
}

# Baixa o MSI da release e confere o SHA256 publicado junto (arquivo .sha256).
function Get-ReleaseMsi([string]$RequestedVersion) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'DBConnectionTester-RMM' }
    if ($RequestedVersion -eq 'latest') {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers -UseBasicParsing
        $tag = [string]$release.tag_name
    } else {
        $tag = 'v' + $RequestedVersion.TrimStart('v')
    }
    if ($tag -notmatch '^v\d+\.\d+\.\d+$') { throw "Versao invalida: $tag" }
    $name = "DBConnectionTester-$tag-win-x64.msi"
    $base = "https://github.com/$Repo/releases/download/$tag"
    if (-not (Test-Path -LiteralPath $DownloadDir)) { New-Item -ItemType Directory -Path $DownloadDir | Out-Null }
    $msi = Join-Path $DownloadDir $name
    Write-Step "Baixando $name..."
    Invoke-WebRequest -Uri "$base/$name" -OutFile $msi -Headers $headers -UseBasicParsing
    $sums = (Invoke-WebRequest -Uri "$base/$name.sha256" -Headers $headers -UseBasicParsing).Content
    if ($sums -is [byte[]]) { $sums = [Text.Encoding]::ASCII.GetString($sums) }
    $expected = ([string]$sums).Trim().Split(' ')[0].ToLowerInvariant()
    $actual = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -notmatch '^[0-9a-f]{64}$' -or $actual -ne $expected) {
        Remove-Item -LiteralPath $msi -Force -ErrorAction SilentlyContinue
        throw "SHA256 do MSI nao confere com a release $tag. Download descartado."
    }
    Write-Step "MSI $tag conferido (SHA256)."
    return $msi
}

function Wait-DbctExit([int]$TimeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $running = @(Get-Process -Name dbct -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $Dbct })
        if ($running.Count -eq 0) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

try {
    if (-not (Test-Administrator)) { throw 'Execute como administrador ou SYSTEM (instalacao para todos os usuarios).' }
    if ($MsiPath) {
        $msi = [IO.Path]::GetFullPath($MsiPath)
        if (-not (Test-Path -LiteralPath $msi -PathType Leaf)) { throw "MSI nao encontrado: $msi" }
    } else {
        $msi = Get-ReleaseMsi $Version
    }

    $autostart = @()
    if (Test-Path -LiteralPath $Dbct -PathType Leaf) {
        $autostart = Get-AutostartDirectories
        Write-Step ("Instalacao existente: " + (Invoke-Dbct @('--version')).Output)
        foreach ($directory in $autostart) {
            $stop = Invoke-Dbct @('run', 'stop', '--data-dir', $directory, '--timeout', [string]$StopTimeoutSeconds)
            # 2 = nenhuma execucao ativa nesse banco: nada a parar.
            if ($stop.ExitCode -eq 0) { Write-Step "Execucao automatica parada para a atualizacao: $directory" }
            elseif ($stop.ExitCode -ne 2) { Write-Warning "Nao foi possivel parar a execucao em ${directory}: $($stop.Output)" }
        }
        if ($autostart.Count -gt 0 -and -not (Wait-DbctExit 30)) {
            Write-Warning 'Ainda ha dbct.exe desta instalacao em execucao; o MSI pode falhar com arquivo em uso.'
        }
    }

    $installFailure = $null
    try {
        Write-Step "Instalando $msi (log: $LogPath)..."
        $arguments = "/i `"$msi`" /qn /norestart ALLUSERS=2 MSIINSTALLPERUSER=`"`" ADD_TO_PATH=$AddToPath /l*v `"$LogPath`""
        $process = Start-Process -FilePath msiexec.exe -ArgumentList $arguments -Wait -PassThru
        switch ($process.ExitCode) {
            0 { Write-Step 'Instalacao concluida.' }
            3010 { Write-Step 'Instalacao concluida; o Windows Installer pediu reinicio (nao reiniciado).' }
            default { $installFailure = "MSI falhou com codigo $($process.ExitCode). Veja $LogPath." }
        }
    } finally {
        # Retoma o que foi parado, com a versao nova ou, se o MSI falhou, a anterior.
        foreach ($directory in $autostart) {
            $start = Invoke-Dbct @('autostart', 'start', '--data-dir', $directory)
            if ($start.ExitCode -eq 0) { Write-Step "Execucao automatica retomada: $directory" }
            else { Write-Warning "Nao foi possivel retomar a execucao em ${directory}: $($start.Output)" }
        }
    }
    if ($installFailure) { throw $installFailure }
    if (-not (Test-Path -LiteralPath $Dbct -PathType Leaf)) { throw "dbct.exe ausente apos a instalacao: $Dbct" }
    Write-Step ('Versao instalada: ' + (Invoke-Dbct @('--version')).Output)
    Write-Step "CLI: $Dbct (no PATH de terminais abertos depois da instalacao)."
    exit 0
} catch {
    Write-Output "[DBCT] FALHA: $($_.Exception.Message)"
    exit 1
}
