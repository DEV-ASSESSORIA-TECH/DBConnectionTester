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

`settings` é um snapshot sanitizado: não possui senha nem connection string. `cycles[].stages[]` contém `stage`, `status`, `elapsedMs`, `extra`, códigos de diagnóstico e sugestão, confiança, mensagens técnicas e códigos estruturados do provider quando disponíveis.

O schema formal está em [`schemas/run-export-v1.schema.json`](schemas/run-export-v1.schema.json). Consumidores devem rejeitar versões desconhecidas de `formatVersion` ou tratá-las como um contrato diferente.

## ZIP de execução

O ZIP contextual contém um CSV, um TXT, o JSON e `manifest.json`. O manifesto usa `formatVersion: 1` e lista o SHA-256 de cada arquivo. Este ZIP é um relatório de uma execução; ele não é o mesmo formato do pacote portátil completo.
