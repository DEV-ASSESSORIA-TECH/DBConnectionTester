# Solução de problemas

## O aplicativo encontrou vários bancos

Escolha explicitamente o armazenamento desejado. O seletor é intencional: não existe prioridade silenciosa entre históricos válidos. A escolha será lembrada pelo `StoreId`.

## Banco inválido ou com versão futura

O arquivo não será alterado. Confirme se ele realmente pertence ao DB Connection Tester e use uma versão da aplicação compatível. Não renomeie um SQLite qualquer para `data.db`.

## Já existe uma execução gravando neste armazenamento

Outra instância mantém o `.run.lock`. O histórico ainda pode ser consultado. Encerre a execução da outra instância antes de iniciar, clonar, restaurar ou empacotar. Se nenhuma instância existir, confirme no Gerenciador de Tarefas antes de remover manualmente um lock residual.

## ProgramData não pôde ser preparado

Aceite o prompt UAC e confirme que a conta pode elevar. Políticas corporativas podem impedir alteração de ACL. O processo principal não deve ser iniciado permanentemente como administrador.

## Não consigo incluir o EXE no pacote portátil

Essa opção exige que o aplicativo atual seja o EXE self-contained single-file. Use o artefato `*-self-contained.exe` da release ou crie o pacote sem EXE.

## Restauração recusada

Verifique se a pasta de destino está vazia. Checksum inválido, manifesto desconhecido, esquema incompatível e caminhos inseguros são recusados deliberadamente. Baixe ou gere o pacote novamente; não edite o ZIP manualmente.

## CSV/TXT não foi criado

A saída contínua é opcional e vem desligada. Ative-a em Configurações e escolha uma pasta gravável. Se falhar durante a execução, o SQLite continua e o aviso aparece no Histórico. Também é possível exportar depois.

## Uma execução aparece como Interrupted

O processo terminou sem finalizar a sessão, por exemplo após queda de energia ou encerramento forçado. Na próxima inicialização sem lock ativo, o estado é recuperado como `Interrupted`; todos os ciclos já confirmados permanecem disponíveis.

## Driver ODBC não encontrado

Instale o driver SQL Anywhere x64 e informe exatamente seu nome. A arquitetura do driver precisa corresponder à aplicação Windows x64.

## Alto DPI

A interface usa escala por DPI e rolagem. Em 150% ou 200%, a página de execução pode exibir barra horizontal para preservar os campos e o gráfico sem reduzir a legibilidade.
