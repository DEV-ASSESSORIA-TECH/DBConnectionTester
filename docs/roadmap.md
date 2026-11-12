# Roadmap

O DB Connection Tester nasceu como testador de conexão com bancos de dados e passou a oferecer testes gerais de DNS, ICMP e TCP. Seu foco é testar repetidamente a disponibilidade ao longo do tempo e registrar evidências para diagnóstico. A expansão preserva os testes de banco e os recursos existentes de execução, estatísticas, perfis, histórico e exportação.

Os novos testes seguirão o fluxo atual: selecionar o banco ou tipo de teste, informar o destino e seus parâmetros, escolher o que será testado e iniciar a execução. Esse fluxo será adaptado e validado para acomodar os novos tipos de teste de forma consistente.

## Escopo a implementar agora

- **HTTP/HTTPS:** disponibilidade, status, redirecionamentos e tempo de resposta de serviços web, com diagnóstico TLS integrado ao HTTPS quando aplicável. TLS não será um teste repetitivo independente.
- **DNS avançado:** consulta de registros, endereços retornados e comparação entre resolvedores.
- **Ping ICMP independente:** testar um hostname/IP sem configurar banco ou serviço; ICMP não utiliza portas.
- **Sondas TCP independentes:** testar conectividade e tempo de conexão para um hostname/IP e porta.
- **IPv4 e IPv6:** opções dos testes de conectividade para selecionar a família de endereços ou comparar ambas separadamente.

## Escopo posterior — explicitamente adiado

- UDP por protocolo.
- MTU do caminho.
- Diagnóstico local de interfaces, gateways, DNS e rotas.
- Captura de pacotes.
- Teste de vazão.

Esses recursos não fazem parte da próxima implementação.

## Melhorias transversais

- Ampliar gráficos de latência, disponibilidade e comparação de resultados.
- Evoluir diagnósticos e correlação entre etapas, distinguindo evidências de possíveis causas.
- Incorporar traceroute como complemento à investigação de falhas, associado à execução, sem torná-lo um teste repetitivo independente ou uma etapa obrigatória de cada ciclo.
- Adaptar interface, perfis, histórico e exportação aos novos testes.
- Adaptar e validar o fluxo de configuração e execução, mantendo a mesma lógica para os testes atuais e novos.
- Revisar a proposta e a apresentação do produto; avaliar uma mudança de nome, ainda sem decisão.

O detalhamento técnico, os requisitos e a ordem de implementação serão definidos no planejamento.

## Pendências após a 3.0.0

Itens conhecidos que ficaram fora da release. Não são funcionalidades novas.

- **Instalador:** no teste de ciclo de vida, uma atualização ou remoção com a CLI aberta não foi recusada como esperado. Reproduzir em VM e corrigir se for do produto.
- **Validação manual do MSI:** assistente nos dois escopos, Windows limpo sem .NET, pendrive e driver SQL Anywhere x64 real.
- **Assinatura:** MSI e executáveis sem Authenticode.
- **SQL Server:** certificado autoassinado falha com a criptografia padrão do SqlClient; expor `--encrypt`/`--trust-server-certificate` e classificar o erro de certificado.
- **Dependências:** Npgsql 10, SqlClient 7.1 (avaliar remover o pin de `Pkcs`), MySqlConnector 3, System.CommandLine 3 e pacotes de teste. Manter os pacotes do runtime na linha 10.x.
- **Trava de escrita:** depende do compartilhamento de arquivos do Windows e não funciona no Linux; necessária antes de uma CLI Linux.
- **Código:** `DatabaseUiState` é estado de interface e está no Core.
- **GitHub:** duas execuções de CI presas desde 11/11 (#31 e #32), com chamado aberto no suporte.

## Ideias: operação 100% pela CLI

Objetivo: executar e acompanhar diagnósticos remotamente pelo terminal (por exemplo, via SSH).

1. `run status`, `history show --follow`, `run stop <id>` e `run --detach`.
2. `profiles add/update/remove` e `settings show/set`.
3. `--credential <nome>` usando o Gerenciador de Credenciais do Windows.
4. `--progress json` com uma linha por ciclo.
5. Armazenamento e pacotes portáteis pela CLI (`storage`, `portable`).
6. CLI para Linux.

Já disponível: queda de sessão e desligamento registram a execução como `Stopped`,
e o módulo `dbct` do Alloy mantém uma execução contínua iniciada com o Windows.

