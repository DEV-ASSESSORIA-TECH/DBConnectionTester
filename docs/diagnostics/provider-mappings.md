# Mapeamentos de providers

Os valores abaixo são a cobertura conhecida do classificador. Todo código não listado é preservado na saída e normalmente resulta em `DBT-DB-099`.

## SQL Server

| Número | Diagnóstico |
|---|---|
| 18456 | `DBT-DB-001` |
| 4060 | `DBT-DB-002` |
| -2 | `DBT-DB-004` ou `DBT-DB-005`, conforme a etapa |
| 53, 64, 233, 10054, 10060, 11001 | `DBT-DB-003` |
| 229, 230 | `DBT-DB-007` |
| 10928, 10929 | `DBT-DB-008` |

São preservados `SqlException.Number` e todos os itens de `SqlException.Errors`.

## PostgreSQL

| SQLSTATE | Diagnóstico |
|---|---|
| 28P01, 28000 | `DBT-DB-001` |
| 3D000 | `DBT-DB-002` |
| classe 08 | `DBT-DB-003` |
| 57014 | `DBT-DB-004` ou `DBT-DB-005`, conforme a etapa |
| 42501 | `DBT-DB-007` |
| 53300 | `DBT-DB-008` |

É preservado `PostgresException.SqlState`.

## MySQL / MariaDB

| Número | Diagnóstico |
|---|---|
| 1045 | `DBT-DB-001` |
| 1049 | `DBT-DB-002` |
| 2002, 2003, 2005, 2013 | `DBT-DB-003` |
| 1044, 1142, 1143 | `DBT-DB-007` |
| 1040 | `DBT-DB-008` |
| 1205 em consulta | `DBT-DB-005` |

São preservados `MySqlException.Number` e `SqlState`.

## SQLite

| Código base | Diagnóstico |
|---|---|
| 5, 6 | `DBT-DB-011` |
| 11 | `DBT-DB-012` |
| 14, 26 | `DBT-DB-010` |
| 23 | `DBT-DB-007` |

São preservados `SqliteErrorCode` e `SqliteExtendedErrorCode`.

## ODBC / SQL Anywhere

| SQLSTATE | Diagnóstico |
|---|---|
| 28000 | `DBT-DB-001` |
| 3D000 | `DBT-DB-002` |
| classe 08 | `DBT-DB-003` |
| HYT00, HYT01 | `DBT-DB-004` ou `DBT-DB-005`, conforme a etapa |
| 42501 | `DBT-DB-007` |
| IM002, IM003 | `DBT-DB-009` |

Todos os itens de `OdbcException.Errors`, com `SQLState` e `NativeError`, são preservados.

## Rede

DNS e TCP preservam `SocketErrorCode` e `NativeErrorCode`. Ping preserva `IPStatus`. Os códigos de timeout, recusa, reset, bloqueio e inalcançabilidade são classificados diretamente por esses enums.

## Alternativa heurística

Mensagens são consultadas somente quando não há código tipado conhecido. Atualmente são reconhecidos termos de TLS/certificado, timeout, autenticação e driver ausente. A confiança é gravada como `Heuristic`.
