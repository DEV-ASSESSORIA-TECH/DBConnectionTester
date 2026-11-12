# Interface de linha de comando

A CLI `dbct.exe` usa o mesmo Core da interface gráfica. Requer Windows x64;
o ZIP Portable CLI inclui o runtime .NET e não depende de WinForms.

## Comandos

```powershell
.\dbct.exe --help
.\dbct.exe --version
.\dbct.exe run --help
.\dbct.exe profiles list --data-dir .\Data --format json
.\dbct.exe profiles show <profile-id> --data-dir .\Data
.\dbct.exe history list --data-dir .\Data --status Completed --page 1 --page-size 50 --format json
.\dbct.exe history show <run-id> --data-dir .\Data --cycles --page-size 50 --format json
.\dbct.exe export <run-id> --data-dir .\Data --format zip --output .\relatorio.zip
.\dbct.exe run status --data-dir .\Data
.\dbct.exe run stop --data-dir .\Data
.\dbct.exe autostart show --data-dir .\Data
.\dbct.exe autostart start --data-dir .\Data
.\dbct.exe autostart disable --data-dir .\Data
```

`profile-id` e `run-id` são GUIDs. Perfis podem ser consultados e executados, mas
não criados, alterados ou excluídos pela CLI nesta versão; nenhum comando da CLI exige
perfil, inclusive a execução automática. Os comandos não abrem
diálogos nem solicitam confirmação. `--version` deve ser usado sozinho.

## Executar

```powershell
# SQLite: o arquivo alvo já deve existir; ele é aberto somente para leitura.
.\dbct.exe run --type sqlite --sqlite-file C:\bancos\alvo.db --count 1 --data-dir .\Data --format json

# Somente TCP, sem ping ou DNS.
.\dbct.exe run --type tcp --host servidor.local --port 443 --dns false --ping false --count 3 --interval 1 --data-dir .\Data

# A variável DB_PASSWORD deve ser fornecida pelo ambiente/gerenciador de segredos.
.\dbct.exe run --type mysql --host servidor.local --user diagnostico --password-env DB_PASSWORD --count 1 --data-dir .\Data

# Perfil salvo, sobrescrevendo opções apenas para esta execução.
.\dbct.exe run --profile <profile-id> --count 10 --interval 1 --password-env DB_PASSWORD --data-dir .\Data --format json
```

| Opção | Uso |
|---|---|
| `--type` | `mysql`, `postgresql`, `sqlserver`, `sqlanywhere`, `sqlite` ou `tcp` |
| `--host`, `--port` | Destino de rede; usa a porta padrão do tipo quando disponível. `--type tcp` exige `--port` |
| `--user`, `--database` | Usuário e nome do banco |
| `--auth` | SQL Server: `windows` ou `sql-login` |
| `--sqlite-file` | Arquivo SQLite existente |
| `--odbc-driver` | Nome do driver SQL Anywhere x64 instalado |
| `--count` | De 1 a 10.000.000 ciclos; padrão sem perfil: 10 (com perfil: o do perfil) |
| `--continuous [true\|false]` | Executar até Ctrl+C; padrão: desabilitado |
| `--interval` | Segundos entre ciclos, de 0 a 3600; padrão sem perfil: 1 (com perfil: o do perfil) |
| `--timeout` | Segundos por etapa, de 1 a 120; padrão: 5 |
| `--dns`, `--ping`, `--tcp`, `--database-test` | Aceitam `true` ou `false`; padrão: habilitadas nas camadas suportadas |
| `--password-env` | Nome da variável de ambiente com a senha |
| `--password-stdin` | Ler uma linha da entrada padrão, sem prompt |
| `--profile` | GUID do perfil existente |

A precedência é: padrões da aplicação → perfil selecionado → opções explícitas.
Sem perfil, o tipo padrão é MySQL e o host é `127.0.0.1`; usuário e outros campos
obrigatórios são validados pelo Core. Senhas não fazem parte dos perfis.
Autenticação que usa senha exige uma fonte explícita; uma senha vazia pode ser
fornecida por essa fonte. As duas fontes são mutuamente exclusivas.

Com `--database-test false`, diagnósticos de rede não exigem usuário, senha ou driver
ODBC. Configurações inválidas não criam armazenamento; `--profile` exige um banco
da aplicação já existente.

Não existe opção `--password`. Evite colocar segredos em argumentos ou no histórico
do shell. A identidade que executa o processo é usada na autenticação integrada
do SQL Server, inclusive em tarefas agendadas.

As configurações persistidas de CSV/TXT contínuo também são respeitadas pela CLI.
Falhas dessa saída opcional permanecem avisos; os resultados principais ficam no
SQLite. Uma execução contínua não tem porcentagem de conclusão. Ctrl+C solicita
parada e aguarda a finalização, registrando a execução como `Stopped`; uma chamada
nativa ODBC pode sobreviver ao tempo de espera, conforme o comportamento existente do
provider. Um segundo Ctrl+C encerra o processo imediatamente; nesse caso a execução
fica como `Running` e é marcada como `Interrupted` na próxima execução.

Fechar o console, encerrar a sessão, desligar o Windows ou parar a tarefa que executa
a CLI também solicitam a parada, com alguns segundos para registrar a execução como
`Stopped`. Encerrar o processo à força (por exemplo, pelo Gerenciador de Tarefas) não
dá esse tempo; a execução é marcada como `Interrupted` na próxima execução.

## Segundo plano e início com o Windows

Por padrão, `run` ocupa o terminal até terminar. Para execuções longas:

| Comando | Comportamento | Continua após |
|---|---|---|
| `run ... --detach` | Executa em segundo plano, devolve o `runId` e libera o terminal | fechar o terminal ou a sessão SSH |
| `run ... --autostart` | Grava os parâmetros, inicia em segundo plano e volta a cada início do Windows | fechar o terminal, logoff e reinício |
| `run status` | Mostra a execução ativa no banco: processo, início, ciclos e se é automática | — |
| `run stop [<run-id>]` | Pede a parada a partir de qualquer terminal e aguarda (`--timeout`, padrão 30 s) | — |
| `autostart show` | Mostra a execução automática do banco: parâmetros, tarefa e estado | — |
| `autostart start` | Retoma a execução automática sem reiniciar o Windows (após `run stop` ou atualização) | — |
| `autostart disable` | Para a execução automática e remove tarefa, parâmetros e senha | — |

```powershell
# Segundo plano, independente do terminal.
.\dbct.exe run --type tcp --host srv-db --port 1433 --continuous true --interval 5 --data-dir .\Data --detach

# Contínua e automática: volta sozinha com o Windows. Não precisa de perfil.
.\dbct.exe run --type sqlserver --host srv-db --user monitor --password-env DB_PASSWORD --interval 5 --data-dir .\Data --autostart
```

`--autostart` implica execução contínua (não aceita `--count` nem `--continuous false`).
Com o terminal **de administrador**, a execução roda como SYSTEM e inicia no boot, sem
ninguém logado. Sem administrador, roda como o usuário atual e inicia no logon dele;
`--autostart-scope system|user` escolhe explicitamente. Os parâmetros ficam na pasta
`autostart` ao lado do `data.db`, restrita a SYSTEM, Administradores e quem configurou;
a senha, quando houver, é cifrada com DPAPI (da máquina para SYSTEM, do usuário no
logon). Um banco aceita uma execução automática; para trocar os parâmetros, use
`autostart disable` e configure de novo. A tarefa aparece no Agendador de Tarefas em
`\DB Connection Tester`.

Se a execução automática terminar (outra execução gravando no banco, falha de
gravação), ela volta em 30 a 60 s. `run stop` encerra a execução automática até o
próximo início do Windows ou até `autostart start`; `autostart disable` a remove.
Para atualizar o DBCT com uma execução automática ativa, pare-a, instale e retome-a;
o [script de RMM](installation.md#instalação-e-atualização-via-rmm) faz isso sozinho. Com autenticação Windows do
SQL Server, a execução como SYSTEM usa a conta do computador.

`run stop` vale para execuções da CLI (normais, `--detach` ou `--autostart`). A
execução da interface gráfica é parada pela GUI. Uma execução por banco continua
valendo: enquanto outra grava, `run` retorna o código 4.

## Armazenamento e concorrência

`--data-dir` indica a pasta de `data.db`, não o banco alvo do teste. Caminhos relativos
são resolvidos a partir do diretório corrente, não da pasta do executável. Em scripts:

```powershell
& (Join-Path $PSScriptRoot 'dbct.exe') history list --data-dir (Join-Path $PSScriptRoot 'Data') --format json
```

A CLI não altera a seleção de banco da GUI no Registro. Sem caminho explícito,
descobre os mesmos armazenamentos e respeita a preferência existente. Em caso de
ambiguidade, retorna código 3 com os candidatos e exige `--data-dir`.

Somente `run` pode criar armazenamento ausente. Sem candidatos, cria no LocalAppData;
copiar o EXE para um pendrive não muda esse padrão. Consultas e exportações exigem
banco existente **e acesso de escrita**, porque a infraestrutura atual atualiza
metadados e configura WAL. Não há modo estritamente somente leitura nesta entrega.

GUI e CLI podem consultar enquanto uma delas executa testes. Apenas um escritor de
execução é aceito por pasta, pelo mesmo `.run.lock`. Não apague o arquivo de lock
para forçar execução: o bloqueio efetivo é o handle aberto. A recuperação de sessões
abandonadas ocorre antes de uma nova execução, sob lock; consultas não recuperam
sessões. Armazenamento WAL em compartilhamento SMB não é suportado.

## Consultas e exportação

`history list` aceita `--from` e `--until` (datas ISO 8601 com fuso), `--profile`,
`--target`, `--status` e `--diagnostic`. Estados: `Running`, `Completed`, `Stopped`,
`Failed`, `Interrupted`. A paginação começa em 1 e aceita até 500 itens por página.
`history show --cycles` inclui uma página de ciclos; sem a opção, retorna detalhes
sem carregar todos os ciclos.

`history cycles <id> --after <n> --limit <m>` lê os ciclos de número maior que `n`
(até 500 por chamada), também durante uma execução ativa. A resposta traz `status` da
execução, `items`, `lastCycle` (use como próximo `--after`) e `hasMore`. Serve para
coletores que acompanham uma execução contínua, como o
[módulo do Grafana Alloy](monitoring.md).

Exportação aceita `csv`, `txt`, `json` e `zip`; o padrão é JSON. Exige uma execução
finalizada e um novo caminho de saída; recusa sobrescrita. Os relatórios mantêm os
[contratos existentes](export-json-schema.md). Exportação para stdout não faz parte
desta versão; o comando escreve no stdout apenas a confirmação com os caminhos.

## Automação, saída e códigos

Execução e consultas aceitam `--format text|json`. O stdout contém o resultado;
progresso e erros vão para stderr. JSON de sucesso usa o envelope abaixo, distinto
do formato do arquivo de relatório:

```json
{
  "formatVersion": 1,
  "data": {}
}
```

Em `run`, `data` contém `runId`, ciclos concluídos, motivo de término, indicador de
falhas, estatísticas, avisos e caminhos opcionais de CSV/TXT. Se a execução falhar
depois de registrada, o envelope também é emitido, com `terminationReason`
`ExecutionFailed`, `failureMessage` e o `runId` para consulta; o código de saída é 5.
O progresso é limitado
a uma atualização a cada 250 ms, mais a atualização final. Erros usam stderr e não
produzem envelope de sucesso. Argumentos inválidos não são repetidos na mensagem
para evitar expor um segredo passado por engano.

| Código | Significado |
|---|---|
| 0 | Comando concluído; execução sem falhas nas etapas habilitadas |
| 1 | Execução concluída com falhas de diagnóstico |
| 2 | Argumentos/configuração inválidos, entrada de credencial inválida, ID inexistente, execução ainda ativa na exportação ou arquivo de destino já existente |
| 3 | Armazenamento indisponível, incompatível ou ambíguo |
| 4 | Outra execução escritora ativa |
| 5 | Falha operacional, de exportação ou da execução (com envelope e `runId` quando a execução chegou a ser registrada) |
| 130 | Cancelamento solicitado |

`Completed` no histórico significa que os ciclos terminaram, mesmo se houve
diagnósticos reprovados. A CLI retorna 1 nesse caso. Avisos de CSV/TXT opcional não
mudam, por si só, o código de diagnóstico; inspecione `warnings` quando necessário.

```powershell
& .\dbct.exe run --type tcp --host servidor.local --port 443 --count 1 --data-dir .\Data --format json 2> .\progresso.log
$code = $LASTEXITCODE
exit $code
```
