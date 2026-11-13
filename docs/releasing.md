# Preparação e publicação da release

## Versão e contratos

`VERSION` é a fonte da versão do projeto, dos artefatos e da tag esperada. Para esta entrega: **3.1.1**. `AssemblyVersion` pode aparecer como `3.1.1.0`; não é outro lançamento. SQLite e os formatos JSON permanecem na versão de contrato 1.

Atualize `VERSION`, README, changelog e notas da release juntos quando a versão mudar. Os nomes dos arquivos de download são gerados pelo script a partir de `VERSION`, sem valores hardcoded.

## Validação local sem publicar

Na raiz, em Windows x64 com .NET 10 SDK. O empacotamento MSI também requer C++ v143,
Windows SDK e o aceite WiX fornecido pelo responsável; veja [pré-requisitos](installation.md#compilar-sem-instalar).

```powershell
dotnet restore DBConnectionTester.sln
dotnet restore src/DBConnectionTester.Desktop/DBConnectionTester.Desktop.csproj -r win-x64
dotnet restore src/DBConnectionTester.Cli/DBConnectionTester.Cli.csproj -r win-x64
dotnet build DBConnectionTester.sln -c Release --no-restore --warnaserror
dotnet test DBConnectionTester.sln -c Release --no-build --no-restore
dotnet list DBConnectionTester.sln package --vulnerable --include-transitive --no-restore
./build/Publish-Release.ps1 -OutputRoot artifacts/release-validation -NoRestore
```

O `OutputRoot` deve ficar no repositório. O script recria `publish/` e `artifacts/` dentro desse destino; use um subdiretório isolado para preservar outros resultados de validação. Sem `-NoRestore`, ele restaura as dependências do runtime antes de publicar.

Não use o comando interno de preparação do armazenamento compartilhado como teste genérico: ele altera a pasta fixa no ProgramData e pode exigir elevação. Para testar armazenamento, prefira bancos temporários e `--data-dir <pasta>`.

## Artefatos

| Arquivo | Uso |
|---|---|
| `DBConnectionTester-v3.1.1-win-x64-framework-dependent.zip` | Requer .NET 10 Desktop Runtime x64. |
| `DBConnectionTester-v3.1.1-win-x64-self-contained.zip` | Inclui runtime, com arquivos separados. |
| `DBConnectionTester-v3.1.1-win-x64-self-contained.exe` | EXE único, com runtime e bibliotecas nativas extraídas quando necessário. |
| `DBConnectionTester-v3.1.1-win-x64-portable-complete.zip` | GUI e CLI em dois executáveis self-contained independentes, mais instruções. |
| `DBConnectionTester-v3.1.1-win-x64-portable-cli.zip` | CLI self-contained sem WinForms, mais instruções. |
| `DBConnectionTester-v3.1.1-win-x64.msi` | GUI e CLI com escopo por usuário ou por máquina, PATH opcional e dados preservados. |
| `SHA256SUMS.txt` e `*.sha256` | Hash dos seis artefatos. |

O script verifica que os ZIPs contêm o EXE e limita o EXE único a 120 MiB. Compare os hashes após transferir os arquivos. Não distribua apenas o apphost pequeno gerado por `dotnet build`: ele depende dos outros arquivos da pasta.

Portable Complete, Portable CLI e MSI foram introduzidos na 3.0.0. O limite de 120 MiB continua exclusivo do
EXE da GUI. Os tamanhos de Complete e CLI são registrados, sem limite presumido.
O Complete duplica parte do runtime para evitar mesclar diretórios de publicação.
O MSI reutiliza exatamente esses executáveis; a inspeção extrai seu CAB e compara
os hashes. Ele é gerado e validado sem instalar o produto na máquina de desenvolvimento.
Sua primeira entrega não possui assinatura Authenticode.

`Test-PortablePackages.ps1` extrai os dois ZIPs em pasta com espaços, inicia a CLI
de outro diretório corrente com lookup de runtime redirecionado, executa teste TCP
local, consulta o histórico e exporta JSON. A validação não requer servidor externo.
Os ZIPs não contêm bancos de usuário. Dependências nativas podem ser extraídas para
uma pasta temporária gravável; drivers ODBC externos não são incorporados.

## Verificação funcional final

- Abra o EXE publicado em uma pasta temporária com `--data-dir` apontando para outro destino temporário.
- Verifique primeiro início, nova execução, parar, bandeja e reabertura.
- Teste perfis e configurações com salvar, descartar e cancelar a navegação.
- Troque claro/escuro na janela aberta; verifique campos de data, combos e rolagem.
- Exporte os quatro formatos; tente novamente após um erro de acesso.
- Crie/restaure pacote portátil e confirme a seleção do banco após salvar e reiniciar.
- Faça um teste com o driver SQL Anywhere x64 real, incluindo timeout e parada. Testes com doubles não substituem compatibilidade com o driver instalado.
- Execute GUI e CLI de um pendrive com `--data-dir` explícito, inclusive após mudar a letra da unidade. Não remova a mídia durante execução.
- Em uma máquina Windows sem .NET instalado, confirme a abertura da GUI e execução da CLI self-contained. Redirecionar lookup no smoke test não substitui essa verificação de ambiente limpo.

## CI e publicação

O [workflow atual](../.github/workflows/publish.yml) valida pull requests, pushes em `main`, tags `v*` e execuções manuais em Windows. Branches de funcionalidade são validadas pela PR, evitando duas execuções idênticas por commit; sem PR aberta, use a execução manual (`workflow_dispatch`). Um commit novo na mesma PR ou branch cancela a validação anterior; execuções de tag nunca são canceladas. Um push em `main` cujo conteúdo (hash da árvore do Git) é idêntico ao do head de uma PR mergeada que já passou no workflow não é validado de novo: o job `Detect already validated content` detecta isso e pula a validação. Se `main` avançou durante a PR, a árvore difere e a validação roda normalmente; exigir PRs atualizadas com `main` antes do merge maximiza esse reaproveitamento. Tags sempre recompilam, pois a release publica os arquivos gerados na própria execução.

O workflow restaura a solução e os runtimes de Desktop/CLI (com cache de pacotes NuGet), audita vulnerabilidades, compila com avisos como erros, executa as duas suítes e gera os seis artefatos. A auditoria falha a execução quando encontra pacote vulnerável, inclusive transitivo. Jobs e etapas longas têm `timeout-minutes`, para que um runner travado falhe em minutos e não em horas.

O responsável deve configurar `DBCT_WIX_ACCEPT_EULA` nas variáveis do repositório
após revisar os termos. O CI não lê o `.env` local nem aceita termos automaticamente.
Toda execução gera o MSI e o valida com ICE e inspeção de conteúdo. O teste de ciclo
de vida é sob demanda: execute o workflow manualmente marcando a opção de instalador.
Nesse caso, o runner descartável executa instalação por usuário com conta padrão
temporária, instalação por máquina, coexistência, reparo, upgrade, downgrade, rollback
e preservação de dados. Esses testes nunca são executados
automaticamente na máquina de desenvolvimento. Em caso de falha, somente os resultados
de teste (`.trx`) e os logs de diagnóstico do instalador são enviados como artefato
temporário por um dia. Esse envio é opcional (`continue-on-error`): uma indisponibilidade
do serviço de artefatos não esconde a falha original nem prende o runner.

O CI usa `--filter "Category!=InteractiveDesktop"` para excluir apenas os dois testes de redimensionamento de janelas de Perfis e Nova execução. O desktop do runner limita as dimensões nativas, impedindo os tamanhos exigidos por esses testes. Os demais testes de UI e comportamento continuam no CI, incluindo a restauração dos campos e eventos de Perfis. Não há `Skip` permanente nos testes.

O comando local acima executa a suíte completa, incluindo esses dois testes. Execute-o em uma sessão gráfica do Windows com espaço suficiente para janelas de até 1680×950 de área cliente antes de publicar. Para executar somente os testes de redimensionamento:

```powershell
dotnet test tests/DBConnectionTester.Tests/DBConnectionTester.Tests.csproj -c Release --no-build --no-restore --filter "Category=InteractiveDesktop"
```

O job de release só roda para uma tag `v*`, após validação, e exige que o nome corresponda a `v<VERSION>`. Uma tag divergente falha. O job publica ou atualiza a GitHub Release usando os artefatos validados. Execuções de branch não publicam uma release.

Branches e PRs bem-sucedidos geram e validam os pacotes sem enviar artefatos ao Actions. Somente tags enviam os treze arquivos (seis artefatos, seis checksums e `SHA256SUMS.txt`) em um único artefato temporário `DBConnectionTester-v<VERSION>-release-bundle`, com `archive: true`, `compression-level: 0` e retenção de um dia. O contêiner de transferência preserva os ZIPs, EXE e MSI originais durante o download. A lista esperada está em `build/Get-ReleaseAssetNames.ps1`; o job de release compara nomes e verifica os hashes antes do upload.

Após criar ou atualizar a release, o workflow confirma que todos os arquivos locais constam nos assets publicados com o mesmo tamanho. Só então exclui o artefato temporário pelo ID fornecido pelo job de validação. A limpeza requer `actions: write` somente no job de release; não remove assets da GitHub Release nem artefatos de outras execuções. Se a publicação ou verificação falhar, o artefato é preservado até expirar, permitindo nova tentativa dentro desse prazo. Após a exclusão ou expiração, execute o workflow completo novamente; repetir apenas o job de release não terá o contêiner disponível.

Arquivos no disco do runner hospedado são temporários. Esta política evita o acúmulo de novos artefatos persistidos no Actions, mas não remove os antigos: uma limpeza inicial deve ser feita separadamente, identificando os artefatos dispensáveis. Não é necessário excluir logs ou releases para aplicar esta política.

Esta preparação de documentação não cria tag nem envia commits. Quando a entrega for aprovada, confira a CI e autorize separadamente a criação/envio da tag e a publicação.
