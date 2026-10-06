# Códigos de diagnóstico

## DNS

| Código | Significado | Sugestão |
|---|---|---|
| `DBT-DNS-001` | Host não encontrado | `DBT-FIX-DNS-001` |
| `DBT-DNS-002` | Falha temporária do resolvedor | `DBT-FIX-DNS-001` |
| `DBT-DNS-003` | Timeout de resolução | `DBT-FIX-DNS-001` |
| `DBT-DNS-004` | Resolução concluída sem endereços | `DBT-FIX-DNS-001` |
| `DBT-DNS-099` | Falha de DNS não classificada | `DBT-FIX-COLLECT-001` |

## Ping / ICMP

| Código | Significado | Sugestão |
|---|---|---|
| `DBT-PING-001` | Timeout no ping | `DBT-FIX-ICMP-001` |
| `DBT-PING-002` | Destino ou rede inalcançável | `DBT-FIX-NET-001` |
| `DBT-PING-003` | ICMP bloqueado ou não permitido | `DBT-FIX-ICMP-001` |
| `DBT-PING-099` | Falha de ping não classificada | `DBT-FIX-COLLECT-001` |

## TCP

| Código | Significado | Sugestão |
|---|---|---|
| `DBT-TCP-001` | Conexão recusada | `DBT-FIX-PORT-001` |
| `DBT-TCP-002` | Timeout ao conectar | `DBT-FIX-TIMEOUT-001` |
| `DBT-TCP-003` | Destino ou rede inalcançável | `DBT-FIX-NET-001` |
| `DBT-TCP-004` | Conexão redefinida ou abortada | `DBT-FIX-PORT-001` |
| `DBT-TCP-005` | Endereço indisponível ou família incompatível | `DBT-FIX-NET-001` |
| `DBT-TCP-099` | Falha TCP não classificada | `DBT-FIX-COLLECT-001` |

## Banco de dados

| Código | Significado | Etapa padrão | Sugestão |
|---|---|---|---|
| `DBT-DB-001` | Autenticação rejeitada | Conexão | `DBT-FIX-AUTH-001` |
| `DBT-DB-002` | Banco não encontrado | Conexão | `DBT-FIX-DB-001` |
| `DBT-DB-003` | Servidor indisponível | Conexão | `DBT-FIX-PORT-001` |
| `DBT-DB-004` | Timeout de conexão | Conexão | `DBT-FIX-TIMEOUT-001` |
| `DBT-DB-005` | Timeout de consulta | Consulta | `DBT-FIX-TIMEOUT-001` |
| `DBT-DB-006` | Falha TLS ou certificado | Conexão | `DBT-FIX-TLS-001` |
| `DBT-DB-007` | Permissão insuficiente | Consulta | `DBT-FIX-PERM-001` |
| `DBT-DB-008` | Limite de conexões atingido | Conexão | `DBT-FIX-CAPACITY-001` |
| `DBT-DB-009` | Driver ausente ou incompatível | Conexão | `DBT-FIX-DRIVER-001` |
| `DBT-DB-010` | Arquivo SQLite inválido ou inacessível | Conexão | `DBT-FIX-SQLITE-001` |
| `DBT-DB-011` | SQLite ocupado ou bloqueado | Consulta | `DBT-FIX-SQLITE-001` |
| `DBT-DB-012` | SQLite corrompido | Consulta | `DBT-FIX-SQLITE-001` |
| `DBT-DB-013` | `SELECT 1` retornou valor inesperado | Consulta | `DBT-FIX-COLLECT-001` |
| `DBT-DB-099` | Falha de banco não classificada | Etapa onde ocorreu | `DBT-FIX-COLLECT-001` |
