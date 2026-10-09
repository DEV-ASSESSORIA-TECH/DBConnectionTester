# Formato JSON de execução

O JSON exportado usa UTF-8, `formatVersion: 1`, nomes em camelCase, horários ISO-8601, enums textuais e durações em milissegundos. Ele é produzido exclusivamente a partir do SQLite.

```json
{
  "formatVersion": 1,
  "run": {
    "runId": "1f611f99-fabc-452d-a375-18e784f8af21",
    "profileId": null,
    "profileName": null,
    "status": "Completed",
    "terminationReason": "PlannedCountCompleted",
    "startedAt": "2026-10-07T13:40:21.1234567+00:00",
    "finishedAt": "2026-10-07T13:40:26.1234567+00:00",
    "applicationVersion": "2.0.0.0",
    "machineName": "WORKSTATION",
    "databaseType": "PostgreSql",
    "target": "db.example:5432",
    "completedCycles": 1,
    "failureMessage": null
  },
  "settings": {
    "databaseType": "PostgreSql",
    "host": "db.example",
    "port": 5432,
    "user": "monitor",
    "database": "app",
    "sqliteFile": "",
    "sqlServerAuthentication": "SqlLogin",
    "odbcDriver": "",
    "testCount": 1,
    "continuous": false,
    "intervalSeconds": 5,
    "timeoutSeconds": 5,
    "dns": true,
    "ping": true,
    "tcp": true,
    "databaseTest": true
  },
  "stageSummaries": [],
  "warnings": [],
  "cycles": []
}
```

O exemplo acima resume a estrutura; os arrays foram omitidos para facilitar a leitura. Um [exemplo completo](examples/run-export-v1.json) documenta ciclos, diagnósticos, avisos e estatísticas.

`settings` é um snapshot sanitizado: não possui senha nem connection string. `cycles[].stages[]` contém `stage`, `status`, `elapsedMs`, `extra`, códigos de diagnóstico e sugestão, confiança, mensagens técnicas e códigos estruturados do provider quando disponíveis.

## Campos e semântica

| Campo | Conteúdo |
|---|---|
| `run` | Identidade, perfil opcional, status, motivo final, datas, versão, máquina, tipo, destino, contagem e falha. |
| `settings` | Parâmetros usados na execução, não os valores atuais do perfil; nunca contém senha. |
| `cycles` | Todos os ciclos persistidos em ordem, com IPs, início, tempo total do banco e etapas. |
| `cycles[].stages` | Cinco etapas; `Skipped` indica etapa não executada. Conexão e consulta têm resultados independentes. |
| `stageSummaries` | Estatísticas por etapa da execução inteira; podem estar ausentes se a sessão foi interrompida antes da finalização. |
| `warnings` | Data, código e mensagem de falhas não fatais de saída. |

Latências são milissegundos; intervalo e timeout no snapshot são segundos. `confidence` usa `Exact`, `Heuristic`, `Fallback` ou nulo. `providerErrorsJson` é uma string contendo JSON serializado, não um array JSON direto. Campos sem diagnóstico ficam nulos; IPs/extra não aplicáveis podem ser strings vazias.

Nos resumos, tentativas excluem etapas puladas e latências consideram sucessos. `successRate` é a propriedade calculada serializada junto com `statistics`. O perfil associado é resolvido no momento da consulta: `profileName` pode refletir um nome alterado e o vínculo pode ser nulo após exclusão; o snapshot permanece preservado.

O schema formal está em [`schemas/run-export-v1.schema.json`](schemas/run-export-v1.schema.json). Consumidores devem rejeitar versões desconhecidas de `formatVersion` ou tratá-las como um contrato diferente.

## ZIP de execução

O ZIP contextual contém um CSV, um TXT, o JSON e `manifest.json`. O manifesto usa `formatVersion: 1` e lista o SHA-256 de cada arquivo. Este ZIP é um relatório de uma execução; ele não é o mesmo formato do pacote portátil completo.

Os três arquivos seguem `run-<UUID>.csv`, `.txt` e `.json`. O manifesto tem `runId`, `createdAt` e `files[]` com `path` relativo e `sha256`. Consulte [schema do manifesto](schemas/run-export-manifest-v1.schema.json) e [exemplo](examples/run-export-manifest-v1.json).

Os schemas descrevem os formatos produzidos. Eles não verificam os hashes dos arquivos nem substituem a inspeção do SQLite e dos caminhos feita na restauração de um pacote portátil. Um schema válido não comprova a origem confiável do arquivo.
