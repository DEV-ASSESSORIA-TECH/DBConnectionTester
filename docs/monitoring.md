# Monitoramento com Grafana Alloy

Para acompanhar falhas e lentidões de uma máquina pelo Grafana, o módulo `dbct` do
`alloyctl` ([repositório scripts](https://github.com/DEV-ASSESSORIA-TECH/scripts/tree/main/monitoring/alloy#módulo-db-connection-tester))
mantém uma execução contínua do DBCT e transforma os ciclos em métricas Prometheus.

```text
dbct run --continuous (tarefa no boot, SYSTEM)  ──grava──▶  data.db
dbct history cycles   (coletor, a cada 30 s)   ◀──lê────   data.db
textfile\dbct\dbct.prom  ──Alloy──▶  Prometheus (job integrations/dbconnectiontester)
```

O DBCT não precisa de nenhuma configuração própria para isso: o módulo usa só a CLI
documentada.

| Recurso da CLI | Uso pelo módulo |
|---|---|
| `run --profile <id> --continuous true --no-progress` | execução contínua sem progresso no stderr |
| `--password-stdin` | senha do perfil, guardada com DPAPI pelo módulo |
| `history list --status Running` | encontra a execução ativa |
| `history cycles <id> --after <n>` | lê só os ciclos novos desde a coleta anterior |
| Encerramento por SIGTERM/SIGHUP | desligar o Windows registra a execução como `Stopped` |

Uma execução por banco continua valendo: enquanto a GUI grava no mesmo banco, a
execução contínua espera, e o coletor acompanha a execução que estiver ativa.

As métricas, consultas PromQL para investigar um horário e a configuração estão no
README do módulo. O detalhe de cada ciclo continua no histórico do DBCT
(`dbct history cycles` ou exportação).
