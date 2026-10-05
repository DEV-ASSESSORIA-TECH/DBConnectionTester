# DB Connection Tester

Aplicativo Windows leve para medir, de forma repetida, as camadas envolvidas no acesso a bancos de dados: resolução DNS, Ping/ICMP, abertura TCP, conexão ADO.NET e uma consulta `SELECT 1`.

## Bancos e providers

| Tipo | Provider .NET | Porta padrão | Campos específicos |
|---|---|---:|---|
| MySQL / MariaDB | `MySqlConnector` | 3306 | Host, usuário, senha e banco opcional |
| PostgreSQL | `Npgsql` | 5432 | Host, usuário, senha e banco opcional |
| SQL Server | `Microsoft.Data.SqlClient` | 1433 | Autenticação Windows ou usuário/senha |
| SAP SQL Anywhere | `System.Data.Odbc` | 2638 | Host, credenciais, banco opcional e nome do driver ODBC |
| SQLite | `Microsoft.Data.Sqlite` | — | Arquivo `.db`, `.sqlite` ou `.sqlite3` existente |
| Somente TCP | — | Editável | Host e porta, sem conexão de banco |

Cada ciclo cria uma nova conexão. O pooling é desativado nos providers que oferecem essa configuração para que o tempo medido represente abertura e autenticação, em vez de reutilização de uma conexão anterior. SQLite é aberto em modo somente leitura e nunca é criado pelo programa.

Para SQL Anywhere, instale o cliente/driver ODBC do SAP SQL Anywhere na mesma arquitetura do executável. O campo **Driver ODBC** usa `SQL Anywhere 17` como padrão e pode ser alterado para o nome registrado na máquina. Se o driver não estiver instalado, a falha será registrada em `DB_Error`.

## Funcionalidades

- Quantidade fixa de ciclos ou execução contínua sem limite.
- Execução assíncrona, intervalo configurável e timeout por etapa.
- Seleção independente de Ping, TCP e teste de banco quando aplicável.
- Minimização para a bandeja, reabertura do painel e status no ícone.
- Abertura do CSV, TXT e pasta de saída.
- Parada manual e encerramento limpo, inclusive no desligamento do Windows.
- CSV e TXT com `AutoFlush`, reduzindo perda de ciclos já concluídos.
- Credenciais mantidas apenas na memória; usuário, senha e connection string não são gravados.
- Nenhum item de Startup, serviço ou tarefa agendada é criado.

Ao selecionar SQLite, os campos de rede são substituídos pelo seletor de arquivo e DNS/Ping/TCP ficam desativados. Em **Somente TCP**, o teste de banco e os campos de credenciais ficam desativados.

## Saídas

O CSV é UTF-8 com BOM e separado por ponto e vírgula. As colunas de banco são genéricas:

`DB_Type`, `DB_Connect_Status`, `DB_Connect_ms`, `DB_Query_Status`, `DB_Query_ms`, `DB_Total_ms` e `DB_Error`.

Etapas não aplicáveis são registradas como `N/A`, com tempo zero. O TXT contém o detalhe de cada ciclo e um resumo de sucessos, falhas e tempos médios.

## Requisitos

- Windows.
- .NET 8 SDK para compilar.
- .NET Desktop Runtime 8 para executar uma publicação dependente do framework.
- Internet no primeiro restore dos pacotes NuGet.
- Driver ODBC do SAP SQL Anywhere apenas para testar esse banco.

## Compilar e testar

```powershell
dotnet restore DBConnectionTester.sln
dotnet build DBConnectionTester.sln --configuration Release
dotnet test DBConnectionTester.sln --configuration Release
dotnet run --project DBConnectionTester.csproj
```

Os testes automatizados validam perfis, portas, providers e connection strings sem depender de servidores reais. A conectividade com cada banco deve ser validada contra uma instância disponível no ambiente de destino.

## Publicar

Framework-dependent, menor e dependente do .NET Desktop Runtime 8:

```powershell
dotnet publish DBConnectionTester.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

Self-contained, maior e sem exigir runtime instalado:

```powershell
dotnet publish DBConnectionTester.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

O resultado fica em `bin\Release\net8.0-windows\win-x64\publish\`.
