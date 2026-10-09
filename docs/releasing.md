# Preparação e publicação da release

## Versão e contratos

`VERSION` é a fonte da versão do projeto, dos artefatos e da tag esperada. Para esta entrega: **2.0.0**. `AssemblyVersion` pode aparecer como `2.0.0.0`; não é outro lançamento. SQLite e os formatos JSON permanecem na versão de contrato 1.

Atualize `VERSION`, README, changelog e notas da release juntos quando a versão mudar. Os nomes dos arquivos de download são gerados pelo script a partir de `VERSION`, sem valores hardcoded.

## Validação local sem publicar

Na raiz, em Windows x64 com .NET 10 SDK:

```powershell
dotnet restore tests/DBConnectionTester.Tests/DBConnectionTester.Tests.csproj
dotnet restore DBConnectionTester.csproj -r win-x64
dotnet build DBConnectionTester.sln -c Release --no-restore --warnaserror
dotnet test tests/DBConnectionTester.Tests/DBConnectionTester.Tests.csproj -c Release --no-build --no-restore
dotnet list tests/DBConnectionTester.Tests/DBConnectionTester.Tests.csproj package --vulnerable --include-transitive --no-restore
./build/Publish-Release.ps1 -OutputRoot artifacts/release-validation -NoRestore
```

O `OutputRoot` deve ficar no repositório. O script recria `publish/` e `artifacts/` dentro desse destino; use um subdiretório isolado para preservar outros resultados de validação. Sem `-NoRestore`, ele restaura as dependências do runtime antes de publicar.

Não use o comando interno de preparação do armazenamento compartilhado como teste genérico: ele altera a pasta fixa no ProgramData e pode exigir elevação. Para testar armazenamento, prefira bancos temporários e `--data-dir <pasta>`.

## Artefatos

| Arquivo | Uso |
|---|---|
| `DBConnectionTester-v2.0.0-win-x64-framework-dependent.zip` | Requer .NET 10 Desktop Runtime x64. |
| `DBConnectionTester-v2.0.0-win-x64-self-contained.zip` | Inclui runtime, com arquivos separados. |
| `DBConnectionTester-v2.0.0-win-x64-self-contained.exe` | EXE único, com runtime e bibliotecas nativas extraídas quando necessário. |
| `SHA256SUMS.txt` e `*.sha256` | Hash dos três artefatos. |

O script verifica que os ZIPs contêm o EXE e limita o EXE único a 120 MiB. Compare os hashes após transferir os arquivos. Não distribua apenas o apphost pequeno gerado por `dotnet build`: ele depende dos outros arquivos da pasta.

## Verificação funcional final

- Abra o EXE publicado em uma pasta temporária com `--data-dir` apontando para outro destino temporário.
- Verifique primeiro início, nova execução, parar, bandeja e reabertura.
- Teste perfis e configurações com salvar, descartar e cancelar a navegação.
- Troque claro/escuro na janela aberta; verifique campos de data, combos e rolagem.
- Exporte os quatro formatos; tente novamente após um erro de acesso.
- Crie/restaure pacote portátil e confirme a seleção do banco após salvar e reiniciar.
- Faça um teste com o driver SQL Anywhere x64 real, incluindo timeout e parada. Testes com doubles não substituem compatibilidade com o driver instalado.

## CI e publicação

O [workflow atual](../.github/workflows/publish.yml) valida pushes de branches, pull requests e execuções manuais em Windows. Restaura, lista vulnerabilidades, compila com avisos como erros, testa e gera os três pacotes. A etapa de auditoria lista vulnerabilidades; o comando não constitui por si só uma regra que falha automaticamente ao encontrar um pacote vulnerável. Examine sua saída antes de publicar.

O CI usa `--filter "Category!=InteractiveDesktop"` para excluir apenas os dois testes de redimensionamento de janelas de Perfis e Nova execução. O desktop do runner limita as dimensões nativas, impedindo os tamanhos exigidos por esses testes. Os demais testes de UI e comportamento continuam no CI, incluindo a restauração dos campos e eventos de Perfis. Não há `Skip` permanente nos testes.

O comando local acima executa a suíte completa, incluindo esses dois testes. Execute-o em uma sessão gráfica do Windows com espaço suficiente para janelas de até 1680×950 de área cliente antes de publicar. Para executar somente os testes de redimensionamento:

```powershell
dotnet test tests/DBConnectionTester.Tests/DBConnectionTester.Tests.csproj -c Release --no-build --no-restore --filter "Category=InteractiveDesktop"
```

O job de release só roda para uma tag `v*`, após validação, e exige que o nome corresponda a `v<VERSION>`. Uma tag divergente falha. O job publica ou atualiza a GitHub Release usando os artefatos validados. Execuções de branch não publicam uma release.

Branches e PRs geram e validam os pacotes sem enviar artefatos ao Actions. Somente tags enviam os sete arquivos (três pacotes, três checksums e `SHA256SUMS.txt`) em um único artefato temporário `DBConnectionTester-v<VERSION>-release-bundle`, com `archive: true`, `compression-level: 0` e retenção de um dia. O contêiner de transferência preserva os ZIPs e o EXE originais durante o download.

Após criar ou atualizar a release, o workflow confirma que todos os arquivos locais constam nos assets publicados com o mesmo tamanho. Só então exclui o artefato temporário pelo ID fornecido pelo job de validação. A limpeza requer `actions: write` somente no job de release; não remove assets da GitHub Release nem artefatos de outras execuções. Se a publicação ou verificação falhar, o artefato é preservado até expirar, permitindo nova tentativa dentro desse prazo. Após a exclusão ou expiração, execute o workflow completo novamente; repetir apenas o job de release não terá o contêiner disponível.

Arquivos no disco do runner hospedado são temporários. Esta política evita o acúmulo de novos artefatos persistidos no Actions, mas não remove os antigos: uma limpeza inicial deve ser feita separadamente, identificando os artefatos dispensáveis. Não é necessário excluir logs ou releases para aplicar esta política.

Esta preparação de documentação não cria tag nem envia commits. Quando a entrega for aprovada, confira a CI e autorize separadamente a criação/envio da tag e a publicação.
