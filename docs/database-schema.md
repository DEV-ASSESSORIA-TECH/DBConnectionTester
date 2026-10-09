# Esquema SQLite v1

O banco do aplicativo é distinto dos bancos testados. Seu esquema é criado transacionalmente por `SqliteApplicationStore`; `PRAGMA user_version = 1` identifica o contrato atual. A fonte normativa das colunas é o SQL em [SqliteApplicationStore.cs](../Services/Storage/SqliteApplicationStore.cs).

UUIDs, enums e datas ISO-8601 são armazenados como `TEXT`; contagens e milissegundos, como `INTEGER`; médias e mediana, como `REAL`. Colunas opcionais usam `NULL`. WAL, foreign keys e `busy_timeout` de 5.000 ms são configurados nas conexões. A estrutura não contém senha nem connection string.

## Tabelas e relacionamentos

| Tabela | Chave | Conteúdo e relações |
|---|---|---|
| `store_metadata` | `singleton_id = 1` | `store_id` único, `storage_scope`, `cloned_from_store_id`, `created_at`, `last_opened_at`. |
| `app_settings` | `setting_key` | `value_json`, `updated_at`; chave `application` para preferências. |
| `connection_profiles` | `profile_id` | Nome e `normalized_name` único, tipo, host, porta, usuário, banco, arquivo SQLite, autenticação SQL Server, driver ODBC, JSON de execução e datas. |
| `runs` | `run_id` | Perfil opcional, status, motivo final, início/fim, versão, máquina, tipo, destino, snapshot JSON, ciclos concluídos e falha. |
| `cycles` | `(run_id, cycle_number)` | Início, IP resolvido, dados de Ping/TCP e tempo total do banco. |
| `stage_results` | `(run_id, cycle_number, stage)` | Status, latência, extra, diagnóstico, sugestão, confiança, mensagens e códigos do provider. |
| `run_stage_summaries` | `(run_id, stage)` | Tentativas, sucessos, falhas, média, mínimo, máximo, mediana, P95, falhas consecutivas e última falha. |
| `run_warnings` | `warning_id` autoincremento | `run_id`, data, código e mensagem de aviso não fatal. |

`runs.profile_id` referencia perfis com `ON DELETE SET NULL`. Excluir um perfil não exclui suas execuções. Ciclos, etapas, resumos e avisos usam relações com exclusão em cascata. O aplicativo não oferece exclusão de histórico nesta versão.

Índices adicionais: início de execução, perfil + início, status + início e diagnóstico não nulo nas etapas. As etapas persistidas são `Dns`, `Ping`, `Tcp`, `DatabaseConnect` e `DatabaseQuery`.

## JSON de preferências

`app_settings.value_json` usa camelCase e enums textuais:

```json
{
  "theme": "System",
  "legacyOutputEnabled": false,
  "legacyOutputDirectory": "C:\\Users\\Example\\Documents\\DBConnectionTester\\Exports"
}
```

`theme` aceita `System`, `Light` e `Dark`. A saída contínua é opcional. Quando habilitada, a pasta é validada no salvamento. A troca de banco pode copiar essas três preferências para o destino. Veja [fluxo de configurações](user-guide.md#configurações).

## JSON de valores padrão de perfil

`connection_profiles.execution_defaults_json`:

```json
{
  "testCount": 1000,
  "continuous": false,
  "intervalSeconds": 5,
  "timeoutSeconds": 5,
  "dns": true,
  "ping": true,
  "tcp": true,
  "databaseTest": true,
  "startInBackground": false
}
```

Quantidade válida: 1 a 10.000.000; intervalo: 0 a 3.600 s; timeout: 1 a 120 s. Os parâmetros de conexão ficam nas colunas do perfil, sem senha.

## Snapshot, diagnósticos e estatísticas

`runs.settings_snapshot_json` registra os parâmetros usados na execução, sem senha; é exportado como `settings` no [JSON de execução](export-json-schema.md). Nome do perfil não é um campo do snapshot: consultas resolvem o nome pelo vínculo ainda existente.

`stage_results.provider_errors_json` é JSON serializado dentro de um campo textual opcional. O JSON exportado mantém esse conteúdo em `providerErrorsJson` como string, não como array embutido. As mensagens passam pela redação de credenciais do classificador.

As estatísticas de latência consideram resultados bem-sucedidos da etapa. Tentativas excluem `Skipped`; taxa de sucesso usa sucessos/tentativas. Mediana e P95 são calculados a partir da distribuição limitada em memória, sem guardar uma lista crescente de todas as latências. Latências acima do limite exato de 120.000 ms compartilham um bucket de overflow; percentis nesse bucket usam o máximo observado. Campos de latência sem sucessos são nulos.

## Estados e confirmação

| Status | Motivo final |
|---|---|
| `Running` | `NULL` enquanto ativa |
| `Completed` | `PlannedCountCompleted` |
| `Stopped` | `StoppedByUser` |
| `Failed` | `ExecutionFailed` |
| `Interrupted` | `ProcessInterrupted` |

Cada ciclo e suas cinco etapas são confirmados em uma transação antes de atualizar o progresso visual. A finalização grava status e resumos. Na recuperação após encerramento abrupto, sessões `Running` tornam-se `Interrupted` somente após obter o lock; seus ciclos confirmados permanecem disponíveis. Resumos podem estar ausentes em sessões interrompidas.

## Compatibilidade e manutenção

Um `user_version` futuro é recusado; arquivos SQLite sem metadados reconhecidos não são convertidos por cima. Clonar gera nova identidade e conserva os dados. Cópias manuais compartilham identidade e são diferenciadas pelo caminho lembrado; use backup consistente para evitar perder conteúdo do WAL.

Este documento descreve o esquema, não uma API para escrita direta. Não edite o banco com o aplicativo em execução. Para integração, prefira os relatórios e seus [schemas versionados](README.md#contratos-formais).
