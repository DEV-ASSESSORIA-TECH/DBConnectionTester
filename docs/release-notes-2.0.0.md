# DB Connection Tester 2.0.0

A versão 2.0 transforma o aplicativo em uma ferramenta portátil com histórico persistente, sem exigir instalação nem manter arquivos obrigatórios ao lado do executável.

## Destaques

- SQLite como fonte primária de configurações, perfis e histórico.
- Armazenamento Local, Compartilhado, Portátil ou Personalizado.
- Nova interface navegável com temas claro e escuro.
- Histórico paginado com filtros, diagnósticos, estatísticas e gráficos.
- Exportações CSV, TXT, JSON e ZIP geradas quando necessário.
- Pacotes portáteis completos, com banco clonado, manifesto, SHA-256 e EXE opcional.
- Perfis de conexão reutilizáveis sem armazenamento de senha.
- Recuperação de execuções interrompidas e lock contra escritores concorrentes.
- Migração para .NET 10 LTS.
- Início com execução atual, atalhos e últimas execuções.
- Nova execução com seletor de perfil, resumo estatístico inicial e formulário responsivo.
- Perfis e Configurações com proteção contra edições não salvas.
- Preferências e seleção de banco confirmadas por um único salvamento, com cópia opcional de preferências, descarte e cancelamento de troca pendente.
- Temas modernizados e troca claro/escuro na janela aberta, sem recarregar os painéis.

## Correções incluídas

- Exportações trabalham fora da thread da interface e reportam erros SQLite com opção de nova tentativa.
- Consultas ODBC não bloqueiam a janela; timeout e parada limitam a espera, preservando os recursos até o driver retornar.
- Cópias manuais de um banco com a mesma identidade respeitam o caminho escolhido após reiniciar.
- Pintura de botões, campos de data e contraste de opções desabilitadas revisados.

![Nova execução na versão 2.0.0](images/execution-panel.png)

## Atualização da versão 1.x

O primeiro início cria `%LOCALAPPDATA%\DBConnectionTester\data.db` quando nenhum armazenamento é encontrado. CSV/TXT contínuos ficam desligados por padrão e podem ser reativados em Configurações. Arquivos CSV/TXT antigos não são importados automaticamente.

Nenhuma senha ou connection string é migrada ou persistida. O EXE pode continuar sendo movido entre pastas e máquinas; use um pacote portátil para transportar também perfis e histórico.

Preferências ficam no banco selecionado. Trocar o banco exige salvar e reiniciar; o banco anterior é preservado. Criar/restaurar um pacote portátil é uma operação separada. Os detalhes estão no [guia dos painéis](user-guide.md).

## Artefatos

- `DBConnectionTester-v2.0.0-win-x64-framework-dependent.zip`
- `DBConnectionTester-v2.0.0-win-x64-self-contained.zip`
- `DBConnectionTester-v2.0.0-win-x64-self-contained.exe`
- `SHA256SUMS.txt` e arquivos `.sha256`

O artefato framework-dependent requer o .NET 10 Desktop Runtime. Os outros dois incluem o runtime.

## Compatibilidade

- Windows x64.
- Esquema SQLite v1.
- JSON de execução `formatVersion: 1`.
- Pacote portátil `formatVersion: 1`.

Consulte o [CHANGELOG](../CHANGELOG.md) e a [documentação principal](../README.md) para detalhes completos.

O [índice técnico](README.md) reúne os schemas de execução/manifestos, o esquema SQLite e os fluxos de armazenamento. A [preparação da release](releasing.md) descreve a validação local e a publicação por tag; esta documentação não implica que a tag já tenha sido publicada.
