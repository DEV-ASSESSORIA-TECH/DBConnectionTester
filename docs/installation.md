# Instalação MSI — GUI e CLI

O MSI Windows x64 inclui `DBConnectionTester.exe`, `dbct.exe`, a licença MIT e as
instruções de uso. Reutiliza os mesmos executáveis self-contained dos portables;
não exige instalar .NET separadamente. A primeira entrega não possui assinatura
Authenticode: obtenha o arquivo pelo canal oficial e confira o SHA-256 publicado.

## Instalação interativa

O assistente em português apresenta a licença do projeto, o escopo, a opção de
PATH e a confirmação. O padrão é instalar somente para o usuário atual.

| Contexto | Binários | Permissões | Menu Iniciar e PATH |
|---|---|---|---|
| Por usuário | `%LOCALAPPDATA%\Programs\DBConnectionTester` | Sem administração | Usuário atual |
| Por máquina | `%ProgramFiles%\DBConnectionTester` | Administrador | Todos os usuários |

A pasta é fixa para cada contexto. GUI e CLI são instaladas juntas. Há atalhos da
GUI e das instruções no Menu Iniciar; não são criados atalhos na área de trabalho,
serviços ou entradas de inicialização automática. O aplicativo não é iniciado ao
concluir a instalação.

“Adicionar CLI ao PATH” vem marcado. Reabra o terminal depois de instalar. O MSI
preserva as demais entradas e não duplica uma entrada equivalente já existente;
se o caminho já pertencia ao usuário, não assume sua remoção na desinstalação.
Atualizações e reparos preservam a escolha anterior.

## Instalação silenciosa

Exemplos para Windows PowerShell, substituindo o caminho do MSI:

```powershell
# Usuário atual, sem elevação.
msiexec /i DBConnectionTester.msi /qn /norestart ALLUSERS=2 MSIINSTALLPERUSER=1 ADD_TO_PATH=1

# Todos os usuários, a partir de terminal elevado.
msiexec /i DBConnectionTester.msi /qn /norestart ALLUSERS=2 MSIINSTALLPERUSER="" ADD_TO_PATH=1

# Sem adicionar PATH.
msiexec /i DBConnectionTester.msi /qn /norestart ALLUSERS=2 MSIINSTALLPERUSER=1 ADD_TO_PATH=0
```

`ADD_TO_PATH` aceita `0` ou `1`. Quando omitido, reutiliza a opção salva do contexto;
sem uma instalação anterior, habilita PATH. O uso de `ProcessStartInfo.ArgumentList`
ou ferramentas de distribuição deve transmitir `MSIINSTALLPERUSER=` como um único
argumento para limpar a propriedade no contexto por máquina. Shells têm regras
diferentes para argumentos vazios; confira o contexto registrado no log.

Para logs, acrescente `/l*v C:\Temp\DBCT-install.log`. Não forneça credenciais do
banco ao MSI: elas pertencem à execução da aplicação. O instalador não solicita
nem armazena esses segredos. Código `0` indica sucesso; `3010` indica solicitação
de reinicialização pelo Windows Installer, sem reinício automático. Falhas devem
ser analisadas pelo log; o instalador não mata processos para prosseguir.

## Instalação e atualização via RMM

Para ferramentas de RMM que executam como SYSTEM (Tactical RMM e similares), use
[`tools/rmm/Install-DBConnectionTester.ps1`](../tools/rmm/Install-DBConnectionTester.ps1).
Ele baixa o MSI da release do GitHub, confere o SHA256 publicado junto, instala para
todos os usuários com a CLI no PATH e não configura nenhum monitoramento:

```powershell
# Última release.
.\Install-DBConnectionTester.ps1

# Versão fixa, ou um MSI já copiado para a máquina (sem acesso ao GitHub).
.\Install-DBConnectionTester.ps1 -Version 3.1.1
.\Install-DBConnectionTester.ps1 -MsiPath C:\Instaladores\DBConnectionTester-v3.1.1-win-x64.msi
```

O mesmo script atualiza. Como o MSI não encerra processos em uso, ele para antes as
execuções automáticas desta instalação (`dbct run stop`, registradas como `Stopped`)
e as retoma ao final com `dbct autostart start`, inclusive se a instalação falhar.
Uma GUI ou CLI aberta por um usuário ainda impede a atualização; o log indica o
arquivo em uso. Saída `0` = sucesso; `1` = falha, com o motivo na saída.

Depois de instalar, configure o que cada máquina monitora com `dbct run ... --autostart`
(veja o [guia da CLI](cli.md#segundo-plano-e-início-com-o-windows)). Na mesma sessão do
RMM, use o caminho completo `C:\Program Files\DBConnectionTester\dbct.exe`: o PATH novo
vale para processos iniciados depois da instalação.

## Atualização, reparo e remoção

Encerre os executáveis da instalação antes de mantê-la. Se estiverem em uso, a UI
solicita encerramento e nova tentativa; a execução silenciosa falha. A verificação
usa os caminhos dos arquivos, não apenas nomes de processo, e não encerra cópias
portáteis em outras pastas.

- Uma versão maior atualiza somente o mesmo contexto, com rollback transacional.
- Downgrade é bloqueado. A mesma versão oferece manutenção; reconstruir um MSI
  não o transforma em uma atualização. Use o pacote original para manutenção.
- Reparar restaura arquivos e integrações sem abrir a aplicação.
- Remover elimina apenas arquivos registrados, atalhos e o PATH pertencente ao
  contexto. Arquivos adicionais não são apagados recursivamente.

Instalações por usuário e por máquina podem coexistir. Nenhuma migra ou remove a
outra automaticamente. Use `where.exe dbct` para verificar o executável encontrado;
um caminho absoluto seleciona uma instalação específica. Use o contexto original
também para manutenção silenciosa e remoção.

## Preservação dos dados

O MSI não cria, abre, migra, seleciona ou exclui bancos, não altera a preferência
funcional `HKCU\Software\DBConnectionTester` e não prepara ProgramData ou suas ACLs.
Seus metadados próprios ficam em `Software\DBConnectionTester.Installer`, no contexto
correspondente. Dados locais continuam em `%LOCALAPPDATA%\DBConnectionTester` por
padrão; compartilhamento permanece no fluxo existente do aplicativo.

Instalar para todos os usuários não cria histórico compartilhado. Perfis, histórico,
relatórios, configurações e bancos personalizados são preservados nas operações de
manutenção. SQL Anywhere continua exigindo o driver ODBC x64 externo.

## Compilar sem instalar

Além do .NET 10 SDK, o empacotamento requer Visual Studio 2022/Build Tools com C++
x64 (v143) e Windows SDK. WiX SDK e UI 7.0.1 são restaurados pelo projeto do instalador.
O build normal de `DBConnectionTester.sln` não depende desses componentes.

O responsável precisa revisar e aceitar os [termos OSMF do WiX](https://docs.firegiant.com/wix/osmf/).
O script somente encaminha um aceite já fornecido; não executa `wix eula accept`.
Fontes: parâmetro `-WixAcceptEula`, variável `DBCT_WIX_ACCEPT_EULA`, a mesma variável
no `.env` local ignorado pelo Git, ou o arquivo de aceite previamente registrado
pelo próprio WiX. Nenhum valor padrão aceita os termos automaticamente.

```powershell
# Somente após revisão e aceite pelo responsável, no .env local:
# DBCT_WIX_ACCEPT_EULA=wix7

./build/Publish-Release.ps1 -OutputRoot artifacts/release-validation

# Reutilizar uma publicação Complete já validada:
./build/Build-Installer.ps1 -PayloadDirectory artifacts/release-validation/publish/complete -OutputDirectory artifacts/msi-build
./build/Test-InstallerPackage.ps1 -MsiPath artifacts/msi-build/DBConnectionTester.msi -PayloadDirectory artifacts/release-validation/publish/complete
```

Os builds isolam o cache WiX por diretório e versão de fixture. As validações ICE
permanecem habilitadas; o serviço Windows Installer deve estar acessível, mesmo para
validação sem instalação. Inspeção de tabelas/CAB e testes das ações imediatas não
instalam o produto. A DLL nativa usa CRT estática e não acrescenta dependência de
.NET, PowerShell ou Visual C++ Redistributable ao computador de destino.

## CI e validação de ciclo de vida

O responsável configura a variável de repositório GitHub Actions
`DBCT_WIX_ACCEPT_EULA` com o aceite revisado. O `.env` local não é enviado ao CI.
Sem a configuração, o workflow explica o requisito e interrompe a geração.
A etapa de ciclo de vida tem limite de 30 minutos; em falha, os logs ficam disponíveis
como artefato temporário de diagnóstico.

No CI, o ciclo de vida não roda automaticamente: dispare o workflow manualmente
(`workflow_dispatch`) com a opção de instalador marcada. PRs, `main` e tags continuam
gerando e inspecionando o MSI.

`Test-InstallerLifecycle.ps1` instala somente quando recebe `-DisposableEnvironment`
e encontra `GITHUB_ACTIONS=true` ou `DBCT_INSTALLER_DISPOSABLE=1`. Não use essa opção
na máquina de desenvolvimento. O orquestrador requer administração e cria uma conta
temporária sem privilégios para a aceitação por usuário. Como o Windows Server trata a política
`DisableMSI` ausente como `1` (bloqueando instalações por usuário sem administração,
erro 1625), o orquestrador define `DisableMSI=0` apenas durante o teste, reproduzindo o
padrão do Windows cliente, e restaura o valor original ao final. Executa também manutenção
por máquina e coexistência, preservando logs no ambiente descartável.

As fixtures usam versões `252.0.0`, `252.0.1`, `252.0.2`, sem alterar `VERSION`.
Somente a última fixture recebe uma falha injetada após a cópia dos arquivos para
testar rollback; essa ação não existe no MSI distribuído. Os cenários cobrem PATH
ligado/desligado/pré-existente, reparo, atualização, downgrade, rollback, arquivos
em uso, CLI, GUI no contexto por máquina e preservação de dados e preferências.

A inspeção estrutural, os testes de ações isoladas e a execução em VM são evidências
distintas. Antes da release, valide também o assistente interativo nos dois contextos,
o uso por conta padrão e uma máquina Windows limpa sem .NET. Não confunda geração
do pacote ou parse do workflow com execução desses testes de instalação.
