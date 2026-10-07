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

`HKCU\Software\DBConnectionTester` guarda `ActiveStoreId`, caminho, escopo e a identidade da mídia portátil já observada.

## Resolução na inicialização

1. `--data-dir <pasta>` ou `--data-dir=<pasta>` vence qualquer preferência.
2. São inspecionados o banco portátil, LocalAppData, ProgramData e o caminho personalizado registrado.
3. A preferência é usada somente se o mesmo `StoreId` ainda existir.
4. Uma mídia portátil nova, diferente da preferência, força seleção explícita.
5. Um único candidato é aberto automaticamente; vários candidatos sem preferência abrem o seletor.
6. Sem candidatos, um banco Local é criado.
7. Bancos inválidos ou futuros interrompem a seleção e nunca são recriados por cima.

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

O arquivo `.run.lock`, na mesma pasta do banco, permite uma única execução escritora por `StoreId`. Outras instâncias continuam livres para consultar e paginar o histórico. Cada ciclo e suas etapas são gravados em uma transação antes de o progresso ser entregue à interface.

## Troca de armazenamento

A troca só fica disponível sem execução ativa. O assistente pode:

- clonar o banco atual para um destino vazio;
- criar um banco vazio;
- selecionar um banco compatível existente.

A clonagem usa `SqliteConnection.BackupDatabase`, atribui um novo `StoreId` e registra `ClonedFromStoreId`. O destino nunca é mesclado ou sobrescrito. O banco anterior permanece intacto e a nova preferência entra em vigor na próxima inicialização.

## ProgramData

O processo principal permanece sem elevação. Quando necessário, ele reinicia o próprio EXE com `runas` e um comando interno fixo. O auxiliar elevado cria somente `%PROGRAMDATA%\DBConnectionTester`, concede `Modify` ao SID integrado de usuários locais e encerra. Nenhum caminho arbitrário é aceito pelo comando elevado.

## Backup e manutenção

O tamanho atual do banco aparece em Configurações. Não há exclusão automática. Para manutenção, encerre qualquer execução, preserve um pacote portátil ou cópia consistente e só então remova dados manualmente. Nunca copie diretamente um `data.db` aberto; use a clonagem ou o pacote portátil.
