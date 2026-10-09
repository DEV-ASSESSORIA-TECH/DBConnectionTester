# Changelog

Todas as alterações relevantes deste projeto são documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Unreleased]

## [2.0.0] - 2026-10-09

### Adicionado

- Armazenamento SQLite versionado para configurações, perfis, execuções, ciclos, diagnósticos, resumos e avisos.
- Descoberta segura de armazenamento Local, Compartilhado, Portátil e Personalizado, com preferência por identidade e caminho e opção `--data-dir`.
- Perfis de conexão sem persistência de senha.
- Histórico paginado com filtros, detalhes, ciclos sob demanda e gráficos.
- Exportação posterior em CSV, TXT, JSON v1 e ZIP com manifesto.
- Criação e restauração de pacote portátil completo com backup SQLite, nova identidade, manifesto e SHA-256.
- Assistente de criação, clonagem e seleção de armazenamento, incluindo preparação elevada e restrita do ProgramData.
- Shell WinForms navegável, temas Sistema/Claro/Escuro e suporte a DPI alto.
- CI em push e pull request, auditoria de pacotes, três artefatos Windows x64, checksums e limite de 120 MiB para o EXE único.
- Início com atalhos, estado da execução atual e três execuções recentes.
- Seletor de perfis em Nova execução e proteção contra alterações não salvas em Perfis e Configurações.
- Salvamento conjunto de preferências e seleção de banco, cópia opcional de preferências, descarte de edições e cancelamento da troca pendente.
- Guia dos painéis com capturas reais, documentação do esquema SQLite e schemas dos manifestos JSON.

### Alterado

- Migração para .NET 10 LTS e componentes WinForms correspondentes.
- SQLite tornou-se a saída obrigatória e primária; CSV/TXT contínuos são opcionais e desligados por padrão.
- Cada ciclo é persistido antes de ser informado à interface.
- Execuções abandonadas são recuperadas como `Interrupted` quando não existe escritor ativo.
- A interface mantém navegação e consulta de histórico durante uma execução, bloqueando somente operações mutáveis.
- Nova execução abre no resumo estatístico, com progresso entre formulário e resultados; formulários reorganizam os campos conforme a largura disponível.
- Temas claro e escuro modernizados, com papéis visuais centralizados e estados de ação, erro, aviso e seleção.
- Carregamento paginado e sob demanda, controles leves e preservação dos painéis entre navegações.

### Corrigido

- Exportações do Histórico executadas fora da thread da interface, com bloqueio de duplicidade, feedback e opção de abrir a pasta após sucesso.
- Tratamento de erros SQLite no carregamento e exportação do Histórico, permitindo nova tentativa.
- Abertura e consulta ODBC fora da thread da interface, com espera limitada por timeout/cancelamento e descarte após a chamada nativa terminar.
- Seleção persistente de caminho quando uma cópia manual do banco compartilha a identidade do original.
- Troca de tema em tempo de execução atualiza os controles nativos sem recriar painéis, recarregar dados ou reiniciar o coordenador apenas por mudança de tema.
- Pintura dos botões, contraste dos controles desabilitados e campos de data no tema escuro.

### Segurança

- Senhas e connection strings são excluídas do banco, snapshots, JSON, ZIP e logs.
- Restauração de pacotes rejeita checksum inválido, travessia de caminho e destino ocupado.
- Apenas uma execução escritora é permitida por armazenamento.

## [1.0.3] - 2026-10-06

### Adicionado

- Três modalidades de distribuição Windows x64: framework-dependent, self-contained ZIP e EXE self-contained único.
- Documentação dos comandos de publicação e requisitos de runtime.

## [1.0.2] - 2026-10-06

### Corrigido

- Invariantes tipados para quantidade, intervalo, porta e timeout.
- Neutralização de fórmulas em campos CSV.
- Preservação conjunta de falhas de execução e finalização.
- Limite de memória da distribuição usada nas estatísticas de latência.

## [1.0.1] - 2026-10-06

### Corrigido

- Testes ODBC deixaram de bloquear a interface.
- O ciclo passou a ser persistido antes da contabilização e atualização visual.
- Encerramento passou a aguardar a finalização segura dos arquivos de saída.

## [1.0.0] - 2026-10-06

### Adicionado

- Testes para MySQL/MariaDB, PostgreSQL, SQL Server, SQL Anywhere, SQLite e TCP genérico.
- Diagnóstico estruturado com códigos internos, sugestões e informações do provider.
- Dashboard com DNS, Ping, TCP, conexão, consulta, estatísticas e tendências.
- Exportação contínua CSV/TXT, operação em bandeja e automação inicial de release.

[Unreleased]: https://github.com/GUILHERME-GARCIATECH/DBConnectionTester/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/GUILHERME-GARCIATECH/DBConnectionTester/compare/v1.0.3...v2.0.0
[1.0.3]: https://github.com/GUILHERME-GARCIATECH/DBConnectionTester/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/GUILHERME-GARCIATECH/DBConnectionTester/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/GUILHERME-GARCIATECH/DBConnectionTester/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/GUILHERME-GARCIATECH/DBConnectionTester/releases/tag/v1.0.0
