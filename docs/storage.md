# Arquitetura de armazenamento

## Princípios

O executável continua portátil: pode ser copiado e executado de qualquer pasta. Os dados funcionais ficam em um banco SQLite identificado por `StoreId`. O Registro do usuário funciona somente como localizador da última escolha e nunca contém perfis, resultados ou credenciais.

O banco usa WAL, chaves estrangeiras, `busy_timeout` de cinco segundos e transações curtas. `PRAGMA user_version` controla migrações transacionais. Um banco com esquema mais novo ou estrutura inválida é reportado e preservado sem alterações.

## Modos

| Escopo | Banco |
|---|---|
| `LocalUser` | `%LOCALAPPDATA%\DBConnectionTester\data.db` |
| `SharedMachine` | `%PROGRAMDATA%\DBConnectionTester\data.db` |
| `Portable` | `<pasta-do-exe>\Data\data.db` |
| `Custom` | `<diretório escolhido>\data.db` |

`HKCU\Software\DBConnectionTester` guarda a seleção em um único valor JSON, `StorageSelection`: `storeId`, `databasePath`, `scope` e `observedPortableStoreId` no modelo lógico. A serialização atual do Registro usa nomes PascalCase (`StoreId`, `DatabasePath`, `Scope`, `ObservedPortableStoreId`) e escopo numérico. Os valores antigos `ActiveStoreId`, `ActiveStorePath`, `ActiveStoreScope` e `ObservedPortableStoreId` ainda são lidos para compatibilidade.

## Resolução na inicialização

1. `--data-dir <pasta>` ou `--data-dir=<pasta>` vence qualquer preferência.
2. São inspecionados o banco portátil, LocalAppData, ProgramData e o caminho personalizado registrado.
3. A preferência é usada somente se identidade e caminho corresponderem ao banco inspecionado. Cópias manuais com o mesmo `StoreId` não substituem silenciosamente o caminho lembrado.
4. Uma mídia portátil nova, diferente da preferência, força seleção explícita.
5. Um único candidato é aberto automaticamente; vários candidatos sem preferência abrem o seletor.
6. Sem candidatos, um banco Local é criado.
7. Se não houver candidato compatível e houver bancos inválidos ou futuros, a abertura é interrompida. Uma escolha explícita inválida também é recusada. Esses arquivos nunca são recriados por cima.

Não existe prioridade silenciosa entre bancos válidos. Essa regra evita abrir um histórico diferente apenas porque o EXE foi movido.

## Esquema inicial

- `store_metadata`: identidade, escopo, origem da clonagem e datas.
- `app_settings`: preferências tipadas serializadas em JSON.
- `connection_profiles`: parâmetros reutilizáveis sem senha.
- `runs`: sessão, snapshot sanitizado e estado final.
- `cycles`: dados gerais de cada ciclo.
- `stage_results`: resultado e diagnóstico por etapa.
- `run_stage_summaries`: estatísticas agregadas.
- `run_warnings`: falhas não fatais, como indisponibilidade da saída legada.

Estados possíveis: `Running`, `Completed`, `Stopped`, `Failed` e `Interrupted`. Na inicialização, sessões abandonadas são marcadas como `Interrupted` apenas quando o lock escritor pode ser adquirido.

## Concorrência

O arquivo `.run.lock`, na mesma pasta do banco, permite uma única execução escritora nessa pasta. Outras instâncias continuam livres para consultar e paginar o histórico. Cada ciclo e suas etapas são gravados em uma transação antes de o progresso ser entregue à interface.

O bloqueio efetivo é o handle exclusivo de escrita do arquivo na pasta, não a presença do arquivo nem uma exclusão global por identidade. Cópias em pastas diferentes têm locks independentes. O arquivo pode continuar existindo após encerrar; seu conteúdo é informativo. A recuperação de sessões interrompidas também exige adquirir esse lock.

## Troca de armazenamento

A troca só fica disponível sem execução ativa. Em **Configurações > Banco de dados**, **Escolher…** seleciona diretamente um banco existente. Também é possível:

- clonar o banco atual para um destino vazio;
- criar um banco vazio;
- selecionar um banco compatível existente.

A clonagem usa `SqliteConnection.BackupDatabase`, atribui um novo `StoreId` e registra `ClonedFromStoreId`. O destino nunca é mesclado ou sobrescrito. Criar ou clonar prepara o arquivo, mas ainda não confirma a troca.

**Salvar configurações** grava as preferências e confirma a seleção. Se **Copiar preferências atuais** estiver marcada, tema e saída automática são gravados também no destino; sem ela, o destino mantém suas preferências. Uma clonagem já inclui as preferências. O serviço valida novamente a identidade do destino antes de salvar e tenta restaurar as preferências anteriores se a confirmação falhar.

Após salvar, o banco atual continua ativo e aparece **Troca pendente — reinicie para aplicar**. É possível cancelar a troca pendente. **Descartar alterações** restaura as edições para a última seleção salva, inclusive uma troca já pendente; não apaga arquivos preparados. O banco anterior permanece preservado. Veja o [fluxo completo](user-guide.md#configurações).

![Fluxo de confirmação da troca de armazenamento](images/settings-storage-flow.svg)

O contrato das tabelas e dos JSON internos está em [Esquema SQLite v1](database-schema.md).

## ProgramData

O processo principal permanece sem elevação. Quando necessário, ele reinicia o próprio EXE com `runas` e um comando interno fixo. O auxiliar elevado cria somente `%PROGRAMDATA%\DBConnectionTester`, concede `Modify` ao SID integrado de usuários locais e encerra. Nenhum caminho arbitrário é aceito pelo comando elevado.

## Backup e manutenção

O tamanho atual do banco aparece em Configurações. Não há exclusão automática. Para manutenção, encerre qualquer execução, preserve um pacote portátil ou cópia consistente e só então remova dados manualmente. Nunca copie diretamente um `data.db` aberto; use a clonagem ou o pacote portátil.
