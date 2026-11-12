# DB Connection Tester 3.0.0

A versão 3.0 leva o diagnóstico para o terminal. A nova CLI `dbct` usa o mesmo motor
da GUI, grava no mesmo histórico e pode ser instalada por MSI ou usada em pacotes
portáteis.

## Destaques

- **CLI `dbct`:** `run` por parâmetros ou perfil salvo, `profiles list/show`,
  `history list/show` e `export` em CSV, TXT, JSON ou ZIP. Veja o [guia da CLI](cli.md).
- **Automação:** envelope JSON no stdout, progresso no stderr, senha por variável de
  ambiente ou entrada padrão e códigos de saída estáveis. Execuções que falham também
  retornam o `runId`.
- **MSI Windows x64:** instalação por usuário (sem administrador) ou por máquina, PATH
  opcional, atalhos e dados preservados em reparo, atualização e remoção. Veja a
  [instalação](installation.md).
- **Novos pacotes:** Portable Complete (GUI + CLI) e Portable CLI.
- **Exportações mais seguras:** uma exportação interrompida não deixa arquivo parcial.

## Atualização da versão 2.x

Nada muda nos dados: o esquema SQLite e os formatos JSON continuam na versão 1, e a
GUI continua usando o mesmo armazenamento. A CLI não altera a seleção de banco da GUI e
nunca cria bancos ao consultar.

A CLI sem perfil executa 10 ciclos com 1 s de intervalo. Com `--profile`, valem os
padrões salvos no perfil.

## Artefatos

| Arquivo | Uso |
|---|---|
| `DBConnectionTester-v3.0.0-win-x64-self-contained.exe` | GUI em EXE único, sem instalação |
| `DBConnectionTester-v3.0.0-win-x64-self-contained.zip` | GUI com runtime, em arquivos separados |
| `DBConnectionTester-v3.0.0-win-x64-framework-dependent.zip` | GUI; requer .NET 10 Desktop Runtime x64 |
| `DBConnectionTester-v3.0.0-win-x64-portable-complete.zip` | GUI + CLI portáteis |
| `DBConnectionTester-v3.0.0-win-x64-portable-cli.zip` | Somente a CLI portátil |
| `DBConnectionTester-v3.0.0-win-x64.msi` | GUI + CLI instaladas |
| `SHA256SUMS.txt` e `*.sha256` | Checksums dos seis artefatos |

O MSI ainda não tem assinatura Authenticode; o Windows pode exibir o aviso do
SmartScreen. Confira o hash antes de instalar.

## Compatibilidade

- Windows x64.
- Esquema SQLite v1, JSON de execução e pacote portátil `formatVersion: 1`.

Pendências conhecidas e próximos passos estão no [roadmap](roadmap.md). Detalhes
completos no [CHANGELOG](../CHANGELOG.md).
