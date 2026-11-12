# DB Connection Tester 3.1.0

A versão 3.1 permite deixar o DBCT monitorando sozinho: execuções em segundo plano,
independentes do terminal, que voltam com o Windows e são acompanhadas e paradas de
qualquer terminal.

## Destaques

- **`dbct run ... --autostart`:** execução contínua que inicia agora e volta a cada
  início do Windows. Em terminal de administrador (ou pelo RMM, como SYSTEM), roda no
  boot sem ninguém logado; sem administrador, no logon do usuário. Não exige perfil:
  os parâmetros ficam ao lado do banco e a senha, cifrada com DPAPI.
- **`dbct run ... --detach`:** execução em segundo plano que sobrevive ao fechamento do
  terminal ou da sessão SSH.
- **`dbct run status` e `dbct run stop`:** ver e parar a execução ativa de qualquer
  terminal. **`dbct autostart show|start|disable`:** consultar, retomar e remover a
  execução automática.
- **Instalação e atualização por RMM:** [`tools/rmm/Install-DBConnectionTester.ps1`](../tools/rmm/Install-DBConnectionTester.ps1)
  instala para todos os usuários e, ao atualizar, para e retoma as execuções
  automáticas.
- **Monitoramento opcional com Grafana Alloy:** `dbct history cycles --after` permite
  ler uma execução ativa incrementalmente; o módulo do `alloyctl` publica falhas e
  lentidões no Prometheus. O DBCT continua funcionando sem o Alloy.
- **Instalador:** textos acentuados corrigidos e assistente com o layout padrão do WiX.

## Exemplo

```powershell
# Terminal de administrador: monitorar um MySQL continuamente, também após reiniciar.
$env:DBCT_DB_PASSWORD = '<senha>'
dbct run --type mysql --host 192.168.1.10 --user dbct_monitor --password-env DBCT_DB_PASSWORD --interval 5 --data-dir C:\DBCT\mysql --autostart
Remove-Item Env:DBCT_DB_PASSWORD
dbct run status --data-dir C:\DBCT\mysql
```

## Atualização da versão 3.0

Dados, perfis e histórico continuam compatíveis (SQLite v1, JSON `formatVersion: 1`).
A saída redirecionada da CLI passa a ser sempre UTF-8: scripts que liam a saída com a
página de código do console devem ler em UTF-8. Fechar o console ou desligar o Windows
agora registra a execução como `Stopped`.

Para atualizar uma máquina com execução automática, use o script de RMM ou pare a
execução (`dbct run stop`), instale e retome (`dbct autostart start`).

## Artefatos

Os mesmos seis da 3.0.0, com `v3.1.0` no nome, seus checksums e `SHA256SUMS.txt`. O
MSI ainda não tem assinatura Authenticode; confira o hash antes de instalar.

Pendências conhecidas estão no [roadmap](roadmap.md). Detalhes completos no
[CHANGELOG](../CHANGELOG.md).
