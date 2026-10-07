# Formato das saídas

## Interface

O cartão da etapa e a grade exibem `[código] mensagem curta`. Ao posicionar o cursor, o tooltip mostra sugestão, provider, código original, SQLSTATE, código nativo e detalhe técnico. Isso mantém o painel compacto.

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

A linha compacta de cada ciclo foi preservada. Para cada falha, uma linha indentada `DIAGNÓSTICO` registra:

- etapa;
- código interno;
- código e texto da ação sugerida;
- provider e códigos originais;
- nível de confiança;
- detalhe técnico.

## JSON e ZIP

A versão 2.0 exporta execuções armazenadas em JSON UTF-8 com `formatVersion: 1`, datas ISO-8601, enums textuais e latências em milissegundos. O contrato e o schema formal estão em [Formato JSON de execução](../export-json-schema.md).

O ZIP do Histórico agrupa CSV, TXT, JSON e um manifesto com SHA-256. Ele é diferente do pacote portátil completo, que contém o banco da aplicação.
