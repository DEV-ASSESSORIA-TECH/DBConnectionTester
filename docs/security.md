# Segurança e privacidade

## Dados que nunca são persistidos

- senha;
- connection string;
- conteúdo de variáveis de ambiente de autenticação;
- segredos necessários apenas para abrir a conexão.

Perfis guardam usuário e parâmetros operacionais, mas não senha. O snapshot de execução é construído a partir de um tipo sanitizado que não possui propriedade de senha. Exportações leem esse snapshot, impedindo que um segredo apenas mantido em memória apareça em JSON ou ZIP.

## Diagnósticos

O classificador remove a senha conhecida, mascara `Password=` e `Pwd=`, normaliza quebras de linha e aplica a redação à mensagem principal e à coleção de erros do provider. Novos providers e classificadores devem incluir teste específico de não vazamento.

Host, porta, usuário, nome do banco, endereço IP, nome da máquina e mensagens técnicas podem ser informações operacionais sensíveis. Proteja o `data.db` e os arquivos exportados conforme a política do ambiente.

## CSV

Campos iniciados por `=`, `+`, `-`, `@`, tabulação ou quebra de linha recebem prefixo seguro antes do escape CSV. Isso reduz o risco de execução de fórmulas ao abrir o arquivo em uma planilha.

## SQLite e concorrência

Foreign keys ficam habilitadas. Escritas relacionadas a um ciclo são transacionais. Uma falha do sink SQLite interrompe a execução; uma falha opcional de CSV/TXT vira aviso persistido. O lock por repositório impede duas execuções escritoras simultâneas, mas não bloqueia consultas.

## Pacotes e caminhos

Pacotes portáteis usam SHA-256 e rejeitam caminhos absolutos ou com travessia `..`. Restaurações usam uma área temporária e só ocupam o destino após validação completa. Destinos existentes nunca são sobrescritos.

## Elevação

O aplicativo principal não solicita privilégios administrativos. A preparação do armazenamento compartilhado inicia um processo auxiliar com `runas`, usa somente o caminho fixo `%PROGRAMDATA%\DBConnectionTester`, aplica permissão `Modify` ao grupo integrado de usuários e encerra.

## Limites

SHA-256 detecta corrupção e adulteração acidental, mas o pacote não possui assinatura criptográfica própria. Distribua artefatos por um canal confiável e compare os checksums publicados. O EXE também não armazena credenciais do Windows nem substitui controles de acesso do sistema operacional.
