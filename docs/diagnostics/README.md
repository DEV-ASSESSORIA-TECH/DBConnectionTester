# Diagnóstico inteligente

O diagnóstico inteligente transforma falhas de DNS, ICMP, TCP e banco em dados estáveis e pesquisáveis. Ele mantém três identidades separadas:

- **código de diagnóstico** (`DBT-...`): identifica a causa entendida pelo aplicativo;
- **código de sugestão** (`DBT-FIX-...`): identifica a ação recomendada;
- **código original do provider**: mantém o número, SQLSTATE ou código nativo entregue pela biblioteca.

O código original nunca é substituído pelo código interno. Quando não existe mapeamento conhecido, o erro externo continua preservado e o aplicativo usa um código `099`, sem inventar uma causa.

## Arquivos

- [Códigos de diagnóstico](diagnostic-codes.md)
- [Sugestões de correção](suggestion-codes.md)
- [Mapeamentos de providers](provider-mappings.md)
- [Formato das saídas](output-schema.md)
- [Segurança e redação](security-and-redaction.md)

## Confiança

- `Exact`: classificação baseada em propriedade tipada, status ou código do provider.
- `Heuristic`: classificação baseada no texto da exceção, usada apenas como alternativa.
- `Fallback`: falha não classificada; consulte o detalhe e o código original.

Os códigos publicados são estáveis: não devem ser renumerados nem reutilizados com outro significado.
