# Documentação — DB Connection Tester 2.0.0

## Uso

- [Guia dos painéis e fluxos](user-guide.md): execução, perfis, histórico, configurações e bandeja.
- [Solução de problemas](troubleshooting.md): armazenamento, exportação, drivers e DPI.
- [Segurança e privacidade](security.md): dados persistidos, redação e distribuição.
- [Notas da versão 2.0.0](release-notes-2.0.0.md) e [changelog](../CHANGELOG.md).

## Dados e portabilidade

- [Arquitetura de armazenamento](storage.md): descoberta, confirmação de troca e concorrência.
- [Esquema SQLite v1](database-schema.md): tabelas, relacionamentos e JSON internos.
- [Formato JSON de execução](export-json-schema.md): relatórios e manifesto do ZIP.
- [Pacotes portáteis](portable-packages.md): backup completo e restauração.
- [Diagnósticos](diagnostics/README.md): códigos, sugestões, providers e formatos de saída.

## Contratos formais

- [Execução JSON v1](schemas/run-export-v1.schema.json).
- [Manifesto de relatório ZIP v1](schemas/run-export-manifest-v1.schema.json).
- [Manifesto de pacote portátil v1](schemas/portable-package-v1.schema.json).

`VERSION`, versão do esquema SQLite e `formatVersion` dos arquivos são conceitos independentes. A aplicação 2.0.0 usa SQLite v1 e os contratos JSON v1.

Os [exemplos](examples/) foram gerados com os serviços reais e dados fictícios para validar a estrutura. Os manifestos descrevem arquivos de validação temporários; seus hashes não são checksums de downloads nem um pacote restaurável fornecido pela documentação.

## Desenvolvimento e publicação

- [Preparação e publicação](releasing.md): validação local, artefatos e comportamento da CI.
- Fontes: [projeto](../DBConnectionTester.csproj), [workflow](../.github/workflows/publish.yml) e [script de empacotamento](../build/Publish-Release.ps1).

As capturas dos cinco painéis estão no guia. Os SVGs de arquitetura e fluxos permanecem em `images/`.
