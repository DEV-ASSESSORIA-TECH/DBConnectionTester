# Segurança e redação

Diagnósticos e arquivos de saída não devem expor credenciais.

O classificador:

- substitui a senha informada nas configurações por `***`;
- mascara valores encontrados após `Password=` ou `Pwd=`;
- remove quebras de linha das mensagens;
- aplica a redação tanto ao detalhe principal quanto à coleção de erros do provider.

O aplicativo não grava a connection string. O nome do banco, host, porta e usuário podem continuar sendo dados operacionais sensíveis; proteja o banco e os arquivos exportados conforme a política do ambiente.

Novos classificadores devem usar propriedades tipadas do provider antes de analisar texto e precisam passar por teste de não vazamento de segredo.

As garantias do SQLite, JSON, ZIP, pacotes portáteis e elevação estão em [Segurança e privacidade](../security.md).
