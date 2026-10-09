# Solução de problemas

## O aplicativo encontrou vários bancos

Escolha explicitamente o armazenamento desejado. A preferência válida usa identidade e caminho. Se você copiou o arquivo manualmente, escolha o caminho da cópia; não copie apenas o `data.db` enquanto ele estiver aberto em WAL.

## Banco inválido ou com versão futura

O arquivo não será alterado. Confirme se ele realmente pertence ao DB Connection Tester e use uma versão da aplicação compatível. Não renomeie um SQLite qualquer para `data.db`.

## Já existe uma execução gravando neste armazenamento

Outra instância mantém o handle de escrita do `.run.lock`. O histórico ainda pode ser consultado. Encerre a execução da outra instância antes de iniciar, clonar, restaurar ou empacotar. A presença do arquivo após encerrar é normal e não exige apagá-lo; se o bloqueio continuar, confira os processos e as permissões da pasta.

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

## A troca de banco não foi aplicada

Selecionar, criar ou clonar não confirma a troca. Clique em **Salvar configurações**, confira a indicação de troca pendente e reinicie. **Descartar alterações** volta à seleção salva; **Cancelar troca** cancela uma seleção já confirmada para o próximo início. Para retornar a outro banco, use **Escolher…**, salve e reinicie.

## A exportação falhou

Verifique a mensagem exibida abaixo das ações do Histórico. Erros de permissão, banco bloqueado ou arquivo de destino existente são reportados sem bloquear a interface. Escolha um nome novo e tente novamente. A exportação inclui a execução inteira, não somente a página visível. Execuções ainda em andamento não podem ser exportadas.

## ODBC atingiu o timeout, mas o driver ainda aparece ocupado

Timeout e cancelamento limitam a espera do aplicativo. Um driver nativo pode continuar executando internamente até retornar; o aplicativo mantém os recursos vivos e os libera depois. Verifique também o timeout e a conectividade no driver instalado.

## Alto DPI e janela pequena

A interface usa escala por DPI, reorganização por largura e rolagem quando o conteúdo não cabe. Nova execução e Perfis mantêm colunas enquanto há espaço suficiente e depois reorganizam os formulários. Amplie a janela ou use rolagem em telas menores; grades podem abreviar textos e oferecer detalhes por tooltip.

## Tema claro ou escuro

Salve a escolha em Configurações para aplicar o tema à janela aberta e às próximas inicializações. A troca preserva controles, dados, seleção e rolagem. O modo Sistema acompanha a preferência do Windows quando o tema é aplicado. Alto contraste usa as cores do sistema.
