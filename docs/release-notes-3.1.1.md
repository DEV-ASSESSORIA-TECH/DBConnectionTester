# DB Connection Tester 3.1.1

Versão de correção. Recomendada para todas as máquinas que monitoram MySQL ou MariaDB.

## Correção

- **MySQL/MariaDB: o monitoramento não gera mais erros de conexão no servidor.** Até a
  3.1.0, a etapa TCP abria uma conexão e a fechava sem o handshake do banco. O MySQL e o
  MariaDB contam isso como conexão abortada: cada ciclo somava um `Aborted_connects` e
  um erro no contador por máquina limitado por `max_connect_errors`. Se esse contador
  chegar ao limite, o servidor bloqueia a máquina cliente ("Host is blocked because of
  many connection errors"), inclusive para os sistemas que usam o banco.

  Na 3.1.1, com a etapa de banco habilitada, a etapa TCP é medida na própria conexão que
  o driver abre para o `SELECT 1`. Nenhum socket extra é aberto. A etapa continua
  separada no histórico, nas exportações e nas métricas, com latência do connect, IP
  remoto e os códigos `DBT-TCP-*` em falhas.

## O que muda nos resultados

- Para alvos MySQL/MariaDB com a etapa de banco habilitada, o IP local da etapa TCP fica
  vazio.
- Se o nome não resolver e nenhum socket chegar a ser aberto, a etapa TCP fica como não
  executada (`Skipped`); a falha aparece no DNS e na conexão.
- Com a etapa de banco desabilitada, um alvo MySQL/MariaDB continua usando a sondagem
  TCP simples. Para monitoramento contínuo, mantenha a etapa de banco habilitada. Os
  demais tipos de banco e o modo Somente TCP não mudam.

Detalhes em [Etapa TCP em MySQL/MariaDB](user-guide.md#etapa-tcp-em-mysqlmariadb).

## Atualização da versão 3.1.0

Dados, perfis, histórico e execuções automáticas continuam compatíveis (SQLite v1, JSON
`formatVersion: 1`). Atualize pelo script de RMM, que para e retoma as execuções
automáticas:

```powershell
.\Install-DBConnectionTester.ps1 -Version 3.1.1
```

## Artefatos

Os mesmos seis da 3.1.0, com `v3.1.1` no nome, seus checksums e `SHA256SUMS.txt`. O
MSI ainda não tem assinatura Authenticode; confira o hash antes de instalar.

Detalhes completos no [CHANGELOG](../CHANGELOG.md).
