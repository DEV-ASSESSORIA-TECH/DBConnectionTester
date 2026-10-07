# Pacotes portáteis

Um pacote portátil completo transfere configurações, perfis e histórico para outra pasta ou máquina sem depender do caminho original do EXE.

## Criação

1. Encerre a execução ativa.
2. Abra **Configurações > Pacote portátil**.
3. Escolha se o EXE deve ser incluído.
4. Selecione um novo arquivo ZIP.

O aplicativo adquire o lock escritor, usa `SqliteConnection.BackupDatabase`, cria um clone com novo `StoreId` e escopo `Portable`, calcula SHA-256 e monta o ZIP. O banco aberto nunca é copiado diretamente.

O EXE só pode ser incluído quando o processo atual é uma publicação single-file. Publicações framework-dependent e self-contained com múltiplos arquivos devem ser distribuídas pelo ZIP oficial da release.

## Conteúdo

```text
manifest.json
Data/
  data.db
DBConnectionTester.exe  # opcional
```

O manifesto v1 contém versão da aplicação, `SourceStoreId`, `PortableStoreId`, caminho do banco, checksums e informações sobre o executável.

## Restauração

1. Encerre a execução ativa.
2. Escolha o ZIP e uma pasta vazia.
3. O aplicativo valida caminhos, versão do manifesto, checksums, esquema, identidade e escopo.
4. Somente após todas as validações a pasta temporária é movida ao destino.

A restauração recusa checksum inválido, banco incompatível, entrada ZIP fora do destino e pasta ocupada. Ela nunca mescla o pacote com um banco existente.

## Diferença para o ZIP de uma execução

- **Exportar ZIP no Histórico:** relatório CSV/TXT/JSON de uma única execução.
- **Criar pacote portátil:** banco completo, manifesto próprio e EXE opcional.
