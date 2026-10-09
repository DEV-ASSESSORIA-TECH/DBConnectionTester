# Formato das saídas

## Interface

Nova execução reúne Resumo estatístico, Ciclos recentes e Tendência; os antigos cartões individuais foram removidos. A grade de ciclos e os detalhes do Histórico apresentam diagnósticos por etapa. Tooltips fornecem códigos e detalhes conforme o campo; o Histórico permite filtrar pelo código de diagnóstico.

## CSV

As colunas anteriores foram mantidas na mesma ordem. Depois delas, cada etapa (`DNS`, `Ping`, `TCP`, `DB_Connect` e `DB_Query`) recebe:

- `Diagnostic_Code`;
- `Suggestion_Code`;
- `Provider`;
- `Provider_Code`;
- `SQL_State`;
- `Native_Code`;
- `Technical_Message`.

Conexão e consulta de banco têm diagnósticos independentes. Campos sem valor ficam vazios.

## TXT

A saída TXT contínua e a exportação posterior têm apresentações próprias. Ambas incluem linhas de ciclo e diagnósticos. O TXT exportado do Histórico inclui etapa, código interno, código da sugestão, confiança e detalhe técnico, além do resumo e dos avisos. A saída contínua também apresenta os dados estruturados do provider e o texto da sugestão:

- etapa;
- código interno;
- código e texto da ação sugerida;
- provider e códigos originais;
- nível de confiança;
- detalhe técnico.

## JSON e ZIP

A versão 2.0 exporta execuções armazenadas em JSON UTF-8 com `formatVersion: 1`, datas ISO-8601, enums textuais e latências em milissegundos. O contrato e o schema formal estão em [Formato JSON de execução](../export-json-schema.md).

O ZIP do Histórico agrupa CSV, TXT, JSON e um manifesto com SHA-256. Ele é diferente do pacote portátil completo, que contém o banco da aplicação.
