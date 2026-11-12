# Monitoramento com Grafana Alloy

O DB Connection Tester funciona sozinho e não conhece o Alloy. Para acompanhar falhas e
lentidões pelo Grafana, o módulo opcional `dbct` do `alloyctl`
([repositório scripts](https://github.com/DEV-ASSESSORIA-TECH/scripts/tree/main/monitoring/alloy#módulo-db-connection-tester))
lê o histórico pela CLI e publica métricas Prometheus.

```text
DBCT (independente)                          Módulo do Alloy (opcional)
dbct run ... --autostart                     coletor a cada 30 s:
  └─ tarefa do Windows, no boot                dbct history list / history cycles
      └─ grava ciclos no data.db  ◀──lê──        └─ textfile\dbct\dbct.prom → Alloy → Prometheus
```

A execução contínua é configurada no próprio DBCT (veja
[segundo plano e início com o Windows](cli.md#segundo-plano-e-início-com-o-windows)); o
módulo só precisa da pasta do banco. Ele usa apenas a CLI pública, em modo de leitura:

| Recurso da CLI | Uso pelo módulo |
|---|---|
| `history list --status Running` | encontra a execução ativa |
| `history cycles <id> --after <n>` | lê só os ciclos novos desde a coleta anterior |
| Saída JSON (`formatVersion` 1), UTF-8 | contrato estável de leitura |

As métricas e as consultas para investigar um horário estão no README do módulo. O
detalhe de cada ciclo continua no histórico do DBCT.
