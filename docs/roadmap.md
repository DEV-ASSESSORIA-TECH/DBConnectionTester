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
