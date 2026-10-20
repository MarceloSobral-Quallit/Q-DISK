# QDisk_Adm — administração dos catálogos do qdisk

O QDisk_Adm (`QDisk_Adm/catalog_admin.py`, Python com Tkinter) é a ferramenta do administrador para:

- editar e validar os catálogos `config/qdisk.catalog.*.json`;
- gerar os manifestos USB das ISOs em `config/manifests/`;
- decidir o método de gravação USB de cada ISO (`usbWritePolicy`);
- verificar as ISOs no Microsoft Defender e gerar uma ISO sem os arquivos barrados;
- publicar o catálogo e os manifestos;
- editar endpoints e IDs dos tokens, e disparar o build completo do projeto.

A janela se chama **Quallit Catalogo ADM v&lt;versão&gt;**; a versão vem da linha `Versao:` de
[`version_info.txt`](../version_info.txt). Um `*` no fim do título indica alterações ainda não salvas.

Fluxos com diagramas: [`docs/FLUXOS_QDisk_Adm.md`](../docs/FLUXOS_QDisk_Adm.md).

## Sumário

1. [Como abrir](#como-abrir)
2. [Pastas, variáveis de ambiente e logs](#pastas-variáveis-de-ambiente-e-logs)
3. [A janela](#a-janela)
4. [Barra superior](#barra-superior)
5. [Lista de itens e console](#lista-de-itens-e-console)
6. [Painel do item](#painel-do-item)
7. [Janela Gravação USB](#janela-gravação-usb)
8. [Microsoft Defender e ISO limpa](#microsoft-defender-e-iso-limpa)
9. [Upload](#upload)
10. [Configurações](#configurações)
11. [Build completo](#build-completo)
12. [Validação do catálogo](#validação-do-catálogo)
13. [Linha de comando](#linha-de-comando)

## Como abrir

Pelo launcher (troca para a raiz do repositório e roda o script, sem repassar argumentos):

```powershell
.\QDisk_Adm\run_qdisk_adm.cmd
```

Direto pelo Python, a partir da raiz do repositório:

```powershell
python .\QDisk_Adm\catalog_admin.py
```

Executável gerado pelo pipeline (etapa "Compilar QDisk_Adm e NetSpeedTest" de
[`tools/build_release_dotnet_4.py`](../tools/build_release_dotnet_4.py)):

```powershell
.\dist\ADM\QDisk_Adm.exe
```

Sem argumentos, abre a janela. Com argumentos, roda a [linha de comando](#linha-de-comando).

O script usa só a biblioteca padrão do Python (Tkinter incluso). Ferramentas externas usadas por funções
específicas:

| Função | Ferramenta |
|---|---|
| Manifesto USB | `Quallit.IsoManifestTool.exe` ou `dotnet` com o projeto [`tools/Quallit.IsoManifestTool`](../tools/Quallit.IsoManifestTool) |
| Montar ISO | PowerShell `Mount-DiskImage` / `Dismount-DiskImage` |
| Verificar no Defender | `MpCmdRun.exe` do Microsoft Defender |
| Gerar ISO limpa | `robocopy` e `oscdimg.exe` (Windows ADK, Deployment Tools) |
| Upload | PowerShell com [`tools/utils/publish_catalogs.ps1`](../tools/utils/publish_catalogs.ps1); `pscp` ou `scp`; `git` |
| Build completo | [`tools/utils/Build-OneFile-Sign_v4.bat`](../tools/utils/Build-OneFile-Sign_v4.bat) |
| Verificadores do token | `python` (ou `py`) com [`tools/utils/generate_token_verifiers.py`](../tools/utils/generate_token_verifiers.py) |

## Pastas, variáveis de ambiente e logs

| Item | Valor |
|---|---|
| Raiz do projeto | `QDISK_ROOT`, se definida; senão a pasta acima de `QDisk_Adm/`. No executável: dois níveis acima de `dist/ADM/` (ou a pasta acima da do `.exe`, se ele estiver em outro lugar). |
| Pasta dos catálogos | `QDISK_CONFIG_DIR`, se definida; senão `<raiz>/config` |
| Catálogos | `<pasta dos catálogos>/qdisk.catalog.*.json` (`rd`, `pnb`, `devteam`, `devteam2`, `tools`) |
| Endpoints | `<pasta dos catálogos>/qdisk.endpoints.json` |
| Tokens | `<pasta dos catálogos>/qdisk.tokens.json` |
| Manifestos gerados | `<pasta dos catálogos>/manifests/` |
| Tempo máximo do helper de manifesto | `QUALLIT_ISO_MANIFEST_HELPER_TIMEOUT_SECONDS` (padrão 600 s; mínimo efetivo 30 s) |
| Log operacional | `<pasta do programa>/log/catalog_admin.log` |
| Log detalhado | `<pasta do programa>/log_debug/catalog_admin_debug_AAAAMMDD_HHMMSS.log` (um por execução) |
| Console do build | `<pasta do programa>/log_debug/build_console_AAAAMMDD_HHMMSS.log` |

"Pasta do programa" é `QDisk_Adm/` no modo Python e a pasta do `.exe` (`dist/ADM/`) no executável.

Quando `QDISK_CONFIG_DIR` aponta para outra pasta, antes do **Upload** e do **Build completo** o QDisk_Adm copia todos
os `*.json` dessa pasta (com subpastas, incluindo `manifests/`) para `<raiz>/config`, que é de onde os scripts leem.

## A janela

```
┌ Catálogo [▾]  Abrir  Novo  Salvar ▾ │ Validar  Destino [▾]  Upload │ Configurações  Build completo ┐
├──────────────────────────────────────────────────────────┬─────────────────────────────────────────┤
│ Filtrar [    ]   Adicionar Duplicar Ativar/desativar Remover │ <nome do item>                     [?] │
│ Status │ USB ID │ Gravação │ Name │ Group │ Arquivo │ URL … │ Name, Group, Status, File URL, …        │
│                                                          │ GRAVAÇÃO USB              Editar…       │
├──────────────────────────────────────────────────────────┤ Preparar ISO…                           │
│ Console                              Limpar  Recolher    │ Verificar no Defender  Gerar ISO limpa… │
│ [hh:mm:ss] [INFO] …                                      │ PRONTO PARA PUBLICAR ✓/✗                │
│                                                          │ Limpar            Atualizar item        │
├──────────────────────────────────────────────────────────┴─────────────────────────────────────────┤
│ <mensagem de status>                                              Versão principal: <versão>       │
└────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

Atalhos: `Ctrl+S` (Salvar) e `Ctrl+Shift+S` (Salvar como…).

## Barra superior

| Controle | O que faz |
|---|---|
| **Catálogo** | Lista os arquivos `qdisk.catalog.*.json` da pasta dos catálogos (e os abertos ou salvos na sessão), pela identificação: `devteam`, `devteam2`, `pnb`, `rd`, `tools`. Arquivo fora desse padrão aparece pelo nome. Ao iniciar, abre o primeiro em ordem alfabética. Escolher outro abre esse arquivo. |
| **Abrir** | Abre qualquer `.json` pelo seletor de arquivos (começa na pasta dos catálogos). |
| **Novo** | Começa um catálogo vazio, em memória, sem arquivo. |
| **Salvar ▾** → **Salvar** | Aplica as alterações pendentes do item, valida e grava no arquivo aberto. Sem arquivo, age como "Salvar como…". |
| **Salvar ▾** → **Salvar como…** | Igual, escolhendo o arquivo. |
| **Salvar ▾** → **Salvar no formato publicado com build/items** | Liga/desliga o formato de gravação. Desligado: array direto `[ … ]`. Ligado: `{ "build": {…}, "items": [ … ] }`, com `build.product` = `Q-DISK`, `build.version` (de `version_info.txt`), `build.buildDateUtc` e `build.generatedBy` = `QDisk_Adm/catalog_admin.py`. Ao abrir um arquivo, a opção acompanha o formato dele. |
| **Validar** | Aplica as alterações pendentes e valida o catálogo inteiro (até 20 erros na mensagem). Ver [Validação](#validação-do-catálogo). |
| **Destino** | Para onde vai o Upload: `Both` (Installer Git + Fallback mateweb), `InstallerGit` (somente installer Git) ou `Fallback` (somente fallback mateweb). Padrão: `Both`. |
| **Upload** | Publica o catálogo aberto. Ver [Upload](#upload). |
| **Configurações** | Endpoints e tokens. Ver [Configurações](#configurações). |
| **Build completo** | Abre a janela do pipeline. Ver [Build completo](#build-completo). |

Ao fechar a janela: se houver cálculo de hashes, manifesto, upload, verificação do Defender, ISO limpa ou build em
andamento, o fechamento é recusado. Com alterações não salvas, pergunta: **Sim** salva e fecha, **Não** descarta e
fecha, **Cancelar** volta.

## Lista de itens e console

**Filtrar** mostra só os itens cujo `name`, `group`, `fileName` ou `fileUrl` contêm o texto (sem diferenciar
maiúsculas).

Colunas: **Status** (Ativo/Inativo; inativos em cinza), **USB ID** (`logical`, `raw`, combinação com `+`, ou
`erro`), **Gravação** (método preferido da `usbWritePolicy`; `(outra imagem)` quando a política é de outro SHA-256),
**Name**, **Group**, **Arquivo**, **URL**, **SHA-256**, **Bytes**.

| Botão | O que faz |
|---|---|
| **Adicionar** | Acrescenta ao fim um item novo com o conteúdo do painel. Valida só esse item (nome repetido é apontado depois, em Validar/Salvar). |
| **Duplicar** | Insere logo abaixo uma cópia completa do item selecionado, com nome `<name>-copy` (ou `-copy2`, `-copy3`…). |
| **Ativar/desativar** | Inverte o estado do item selecionado. Item inativo é gravado com `enabled: false`; ativo é gravado sem o campo `enabled`. |
| **Remover** | Remove o item selecionado, após confirmação. |

Trocar a seleção com alterações não aplicadas no painel aplica essas alterações ao item anterior (em memória); se
não passarem na validação, a seleção volta.

**Console** registra as operações com hora e nível (avisos e erros em cor própria) e grava o mesmo nos logs.
**Limpar** apaga o texto exibido; **Recolher**/**Expandir** esconde ou mostra a área.

## Painel do item

Campos: **Name**, **Group**, **Status** (Ativo/Inativo), **File URL**, **Arquivo de imagem** (`fileName`),
**MD5**, **SHA-256**, **Bytes** (`imageSizeBytes`) e **Descrição (exibida no app)** (até 600 caracteres).

| Botão | O que faz |
|---|---|
| **?** | Resumo rápido de uso. |
| **Hashes…** | Escolhe um arquivo (começa em `~/Downloads`; filtros TIBX/ISO) e calcula MD5, SHA-256 e tamanho com barra de progresso. Preenche esses campos e o **Arquivo de imagem**; se **Name** estiver vazio, sugere um a partir do nome do arquivo. Para arquivo que não é `.tibx`/`.iso`, pede confirmação. **Group** e **File URL** ficam por conta do administrador. |
| **GRAVAÇÃO USB** → **Editar…** | Abre a [janela Gravação USB](#janela-gravação-usb). O texto da seção resume a política (método, permitidos, rótulo) e o `usbIdentification`. Para `.tibx` sem política: "Não se aplica: o TIBX é copiado para o USB (NTFS)." |
| **Preparar ISO…** | Fluxo completo de uma ISO, descrito abaixo. |
| **Verificar no Defender** / **Gerar ISO limpa…** | Ver [Microsoft Defender e ISO limpa](#microsoft-defender-e-iso-limpa). |
| **Limpar** (rodapé) | Esvazia o painel e tira a seleção. |
| **Atualizar item** (rodapé) | Valida o catálogo com o item editado e grava o item na lista (em memória). |

Alterações no painel só entram no catálogo com **Atualizar item** (ou ao trocar de item, validar, salvar, fazer
upload, preparar a ISO ou verificar no Defender, que aplicam antes). O subtítulo do painel mostra
"alterações não aplicadas" enquanto houver diferença.

### Preparar ISO…

Exige um item selecionado. Escolha a ISO uma vez (o seletor começa em `~/Downloads/qdisk`; só aceita `.iso`). Em
sequência:

1. **Manifesto USB.** Usa o helper `Quallit.IsoManifestTool`, procurado nesta ordem: `Quallit.IsoManifestTool.exe`
   na pasta do programa; `<raiz>/dist/ADM/Quallit.IsoManifestTool.exe`; `dotnet run --project
   tools/Quallit.IsoManifestTool/Quallit.IsoManifestTool.csproj -c Release`. Se o helper falhar, o erro é exibido e
   o fluxo para. Só quando nenhuma das três opções existe o QDisk_Adm usa a análise parcial em Python (monta a ISO
   pelo Windows), marcada `analysisEngine: python-fallback`, que não gera sugestão de gravação.
2. Grava `<pasta dos catálogos>/manifests/<nome-da-iso>.manifest.json` e põe o resumo em `usbIdentification` do
   item, com `manifestUrl` = `<primary.baseUrl>/manifests/<nome-da-iso>.manifest.json` (de
   `qdisk.endpoints.json`).
3. Preenche MD5, SHA-256 e tamanho a partir do manifesto e o **Arquivo de imagem**, se vazio (se for outro nome,
   avisa no console).
4. **Gravação sugerida.** Se o item não tem política, sugere uma pela análise; se a política é de outra imagem
   (SHA-256 diferente), refaz a sugestão mantendo o **Rótulo do pendrive** já escolhido; se é desta imagem, mantém.
5. **Verificação no Defender** da mesma ISO (sem nova escolha de arquivo).

Revise o resultado (**Editar…** e **PRONTO PARA PUBLICAR**) e salve.

### PRONTO PARA PUBLICAR

Lista com ✓/✗ do que o item tem:

- URL do arquivo; nome do arquivo; hashes e tamanho; descrição;
- só para `.iso`: manifesto USB com análise completa (`analysisEngine: core-1`); gravação USB da imagem atual
  (mostra método e rótulo); verificação no Defender da imagem atual sem ameaças.

## Janela Gravação USB

Aberta por **Editar…**. As escolhas ficam no painel e só valem depois de **Atualizar item**.

| Controle | Campo em `usbWritePolicy` |
|---|---|
| **Definir o método de gravação deste item** | Desmarcado: o item fica sem política. |
| **Preferido** | `preferredMethod` |
| **Permitidos** | `allowedMethods`: `logical-fat32` (Cópia lógica FAT32), `uefi-ntfs` (UEFI:NTFS), `raw` (RAW/DD), `fat32-split-wim` (FAT32 com split do WIM) |
| **Permitir alterar a imagem (ex.: dividir install.wim)** | `allowContentTransformations` (e `preserveSourceImage` = o contrário) |
| **Motivo** | `note` |
| **Rótulo do pendrive** | `volumeLabel` (vazio: o rótulo da ISO) |
| **Sugerir pela análise** | Preenche os controles a partir da análise gravada no manifesto do item. |
| **Concluir** | Fecha a janela. |

`imageSha256` da política é o SHA-256 da imagem a que ela se refere; `preferredBootTargets` (`bios`/`uefi`) vem da
sugestão e só é mantido enquanto o método preferido não mudar.

**Sugerir pela análise** só funciona com análise `core-1` (helper .NET). O método preferido é o recomendado pela
análise, e os permitidos são os métodos viáveis que não alteram a imagem. Sem análise, com análise parcial ou sem
método que preserve a imagem, mostra o motivo e não sugere.

Regras da política (validadas ao aplicar o item):

- só para imagem ISO: com `fileName` preenchido, ele deve terminar em `.iso`;
- `allowedMethods` não vazio, sem repetição, só com os quatro métodos; `preferredMethod` entre os permitidos;
- `fat32-split-wim` exige **Permitir alterar a imagem**;
- `imageSha256` igual ao `sha256` do item;
- `volumeLabel`: só letras, dígitos, `_` e `-`; até 11 caracteres se algum método permitido for FAT32
  (`logical-fat32`, `fat32-split-wim`), senão até 32; recusado em política só com `raw` (RAW/DD mantém o rótulo da
  ISO).

## Microsoft Defender e ISO limpa

**Verificar no Defender** — exige item selecionado; escolha a ISO (seletor em `~/Downloads/qdisk`). O QDisk_Adm
calcula o SHA-256 da ISO, monta a ISO e roda `MpCmdRun.exe -Scan -ScanType 3 -File <unidade> -DisableRemediation`
(sem remediação: nada vai para a quarentena). O resultado vai para o campo `imageScan` do item:

- `threats`: ameaças (nome e arquivo relativo à raiz da ISO);
- `unclassified`: itens listados pelo Defender como `Unknown`;
- `systemToolCopies`: `.exe` com o mesmo nome de um `.exe` de `System32`/`SysWOW64` (heurística para a regra ASR de
  ferramentas do sistema copiadas);
- `engine`, `scannedAtUtc`, `imageFileName`, `imageSha256`.

O resumo aparece abaixo dos botões e indica quando a verificação é de outra imagem. O `MpCmdRun.exe` é procurado na
plataforma mais nova em `%ProgramData%\Microsoft\Windows Defender\Platform\` e, depois, em
`%ProgramFiles%\Windows Defender\`.

**Gerar ISO limpa…** — exige uma verificação já guardada no item e o `oscdimg.exe` (Windows ADK; procurado em
`%ProgramFiles(x86)%\Windows Kits\*\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\` e no `PATH`).

1. Lista os arquivos da verificação: ameaças já marcadas, cópias de ferramentas do Windows desmarcadas.
2. **Gerar ISO sem os marcados** → escolha a ISO original → escolha onde salvar (sugere `<nome>_clean.iso`).
3. Copia o conteúdo com `robocopy` sem os marcados e gera a ISO com `oscdimg` (UDF, mesmo rótulo de volume, boot BIOS
   por `boot\etfsboot.com` e/ou UEFI por `efi\microsoft\boot\efisys.bin`). Sem nenhum desses dois arquivos, recusa.
4. Mostra caminho e SHA-256 da ISO limpa. O catálogo não é alterado: a ISO limpa é outra imagem; publique-a, aponte o
   **File URL** para ela e rode **Preparar ISO…** com ela.

## Upload

Publica o catálogo aberto e os manifestos.

1. Exige catálogo salvo em arquivo e o script `tools/utils/publish_catalogs.ps1`.
2. Aplica as alterações pendentes do item e valida o catálogo.
3. Se algum item ativo tem ameaça na última verificação do Defender (da imagem atual), pede confirmação.
4. Com alterações não salvas: **Sim** salva e envia, **Não** envia a versão do disco, **Cancelar** interrompe.
5. Confirma arquivo e destino; sincroniza os JSON (quando `QDISK_CONFIG_DIR` é outra pasta).
6. Roda:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\utils\publish_catalogs.ps1 -CatalogPath <catálogo> -Target <Both|InstallerGit|Fallback>
   ```

7. Mostra a saída no console e, no fim, um resumo (catálogo, fallback, Git installer, installer remoto). A saída
   completa fica no log detalhado.

O que o script faz (`publish_catalogs.ps1`):

- aceita só catálogos `qdisk.catalog.pnb.json`, `qdisk.catalog.rd.json`, `qdisk.catalog.tools.json` e
  `qdisk.catalog.devteam.json`; qualquer outro nome (como `qdisk.catalog.devteam2.json`) é recusado com
  "Catalogo nao oficial para upload automatico";
- leva junto todos os `config/manifests/*.manifest.json`;
- **Fallback**: copia catálogo e manifestos por `pscp` (ou `scp`) para `<user>@<host>:<remotePath>/` e confere cada
  JSON por HTTP em `fallback.publicBaseUrl` (comparação do conteúdo JSON por SHA-256);
- **InstallerGit**: clona o repositório `installerGit.repositoryUrl`, copia para `installerGit.targetSubdir`
  (e `…/manifests`), faz commit (`chore: publica catalogos qdisk`) e push, e confere em `primary.baseUrl` com até 5
  tentativas a cada 10 s.

Origem dos parâmetros no script, em ordem de prioridade: variáveis `QUALLIT_UPDATE_*` / `QUALLIT_PUBLISH_*`
(host, porta, host key, usuário, caminho remoto, senha SSH) e `QUALLIT_INSTALLER_*` (URL, pasta, branch, subpasta,
URL de verificação); depois `config/qdisk.endpoints.json`; depois valores de `tools/utils/publish_update_package.ps1`,
se existir. Senha SSH: variável de ambiente ou `SshPassword` em `config/secret.md` (modelo:
[`config/secret.md.example`](../config/secret.md.example)). O script também lê variáveis de um arquivo `.env`
(`tools/.env`, `tools/publish_update.env`, `.env` ou `config/publish_update.env`, o primeiro que existir).

Ensaio sem enviar nada (direto pelo script; o upload do QDisk_Adm não usa esta opção):

```powershell
.\tools\utils\publish_catalogs.ps1 -CatalogPath .\config\qdisk.catalog.tools.json -Target Fallback -DryRun
```

## Configurações

Janela **Configurações do projeto** com duas abas. **Salvar** grava as duas; **Cancelar** fecha sem gravar;
**Restaurar padrões** preenche os valores padrão do código (só grava com **Salvar**).

**Endpoints** → `<pasta dos catálogos>/qdisk.endpoints.json`

| Campo | Chave | Padrão do código |
|---|---|---|
| URL principal | `primary.baseUrl` | `https://installer.quallit.com.br/qdisk/` |
| URL pública fallback | `fallback.publicBaseUrl` | `https://mateweb.ddns.net/qdisk/` |
| Host SSH fallback | `fallback.host` | `mateweb.ddns.net` |
| Porta SSH fallback | `fallback.port` | `2222` |
| Host key SSH fallback | `fallback.hostKey` | vazio |
| Usuário SSH fallback | `fallback.user` | `qdisk-update` |
| Caminho remoto fallback | `fallback.remotePath` | `/store/repositorio/qdisk` |
| Repositório Git installer | `installerGit.repositoryUrl` | `https://github.com/quallitdesenv/quallit-installer.git` |
| Subpasta Git installer | `installerGit.targetSubdir` | `public/qdisk` |

Os valores em uso são os do arquivo; os padrões acima só entram com **Restaurar padrões**. Porta entre 1 e 65535;
todos os campos, menos a host key, são obrigatórios.

**Tokens** → `<pasta dos catálogos>/qdisk.tokens.json`

- Para cada perfil (`RD`, `PNB`, `DevTeam`): **Nome exibido**, **ID interno** (exatamente 6 dígitos) e
  **Catálogo associado** (arquivo `.json`; padrões `qdisk.catalog.rd.json`, `qdisk.catalog.pnb.json`,
  `qdisk.catalog.devteam.json`).
- **Perfil downloader** (um dos três) e **Data base downloader** (`AAAA-MM-DD`).

Nenhum perfil tem `qdisk.catalog.tools.json` ou `qdisk.catalog.devteam2.json` como catálogo associado.

Se algo da aba Tokens mudou, o QDisk_Adm roda `tools/utils/generate_token_verifiers.py`, que gera
`config/qdisk.tokens.app.json` (o arquivo embutido no app) a partir de `config/qdisk.tokens.json`. Sem `python`/`py`
no `PATH` ou sem o script, avisa no console que o próximo build regera.

## Build completo

Janela **Build completo**:

| Controle | O que faz |
|---|---|
| **Executar build completo** | Confirma ("bump de versao, publicacao do Git installer, fallback e Git do projeto"), sincroniza os JSON e roda `cmd.exe /c tools\utils\Build-OneFile-Sign_v4.bat`, que chama `python tools\build_release_dotnet_4.py` sem argumentos. |
| **Cancelar build** | Após confirmação, encerra o processo e seus filhos (`taskkill /T /F`). |
| **Console do build** | Saída do pipeline ao vivo, também gravada em `log_debug/build_console_*.log`. |

A linha **Log oficial** mostra o log do pipeline (`build_v<n>_AAAAMMDD_HHMMSS.log`) quando ele o anuncia. No fim, um
resumo com código de saída, versão nova e o estado das etapas "Sincronizar Git installer", "Publicar fallback remoto"
e "Sincronizar Git projeto". A janela não fecha com o build em execução. Etapas do pipeline:
[`docs/DOCUMENTACAO_TECNICA.md`](../docs/DOCUMENTACAO_TECNICA.md#pipeline).

## Validação do catálogo

Usada por **Validar**, **Salvar**, **Atualizar item**, **Upload** e pela linha de comando:

- catálogo com ao menos um item (exceto arquivo chamado `qdisk.catalog.devteam.json`, que pode ficar vazio);
- `name` obrigatório e único; `group` obrigatório; `fileUrl` obrigatório, começando com `http://` ou `https://`;
- `fileName`, se houver: só o nome (sem pasta), terminando em `.tibx`, `.iso` ou `.img`;
- `md5` com 32 e `sha256` com 64 dígitos hexadecimais; com `sha256`, `imageSizeBytes` maior que zero;
- `usbIdentification`: modo `logical` (com `treeHash`, `manifestUrl` HTTP/HTTPS ou relativo, `fileCount`,
  `totalBytes`, sentinelas com `path`, `size` e `sha256`) ou `raw` (com `imageSha256` e `sizeBytes`);
- `usbWritePolicy` conforme a [janela Gravação USB](#janela-gravação-usb);
- `description` com até 600 caracteres.

Formatos aceitos ao abrir: array direto `[ … ]` ou objeto com `items`. Campos que o QDisk_Adm não edita (por exemplo
`imageScan`) são preservados ao salvar.

## Linha de comando

Execute a partir da raiz do repositório:

```powershell
python .\QDisk_Adm\catalog_admin.py <opções>
```

Cada execução faz uma única ação, na primeira que estiver presente nesta ordem: `--validate`, `--export-package`,
`--generate-manifest`, `--generate-missing-manifests`, `--scan-iso`, `--clean-iso`, `--suggest-write-policies`.
Sem nenhuma delas, abre a janela.

| Opção | Valor | Padrão | Uso |
|---|---|---|---|
| `--validate` | catálogo | — | Valida; imprime `OK: <arquivo> (<n> itens)` ou os erros (código de saída 1). |
| `--export-package` | origem destino | — | Valida a origem e grava o destino no formato `{build, items}`. |
| `--generate-manifest` | ISO | — | Gera o manifesto e imprime o resumo `usbIdentification`. |
| `--generate-missing-manifests` | catálogo | — | Gera os manifestos que faltam e atualiza o catálogo. |
| `--image-dir` | pasta | `~/Downloads/qdisk` | Pasta das imagens locais para `--generate-missing-manifests`. |
| `--out` | pasta | `<pasta dos catálogos>/manifests` | Saída dos manifestos (`--generate-manifest` e `--generate-missing-manifests`). |
| `--manifest-url-base` | URL ou caminho | `<primary.baseUrl>/manifests` | Base do `manifestUrl` gravado no resumo. |
| `--refresh` | — | desligado | Com `--generate-missing-manifests`: reanalisa também as ISOs que já têm `usbIdentification`. |
| `--suggest-write-policies` | catálogo | — | Mostra a `usbWritePolicy` sugerida para cada ISO. |
| `--apply` | — | desligado | Com `--suggest-write-policies`: grava as sugestões (valida antes). |
| `--overwrite` | — | desligado | Com `--suggest-write-policies`: substitui políticas existentes. |
| `--scan-iso` | ISO | — | Verifica no Defender; imprime o JSON do resultado e o resumo. |
| `--catalog` | catálogo | — | Com `--scan-iso` e `--item`: grava `imageScan` nos itens. |
| `--item` | nomes | vazio | Nome do item, ou vários separados por vírgula. |
| `--clean-iso` | ISO | — | Gera a ISO sem os arquivos de `--exclude`. |
| `--clean-out` | arquivo | — | Obrigatório com `--clean-iso`: caminho da ISO limpa. |
| `--exclude` | arquivos… | nenhum | Com `--clean-iso`: caminhos relativos à raiz da ISO. |
| `-h`, `--help` | — | — | Mostra a ajuda. |

O `run_qdisk_adm.cmd` não repassa argumentos; para a linha de comando use o `python` direto.

### Exemplos

Validar catálogos:

```powershell
python .\QDisk_Adm\catalog_admin.py --validate .\config\qdisk.catalog.tools.json
python .\QDisk_Adm\catalog_admin.py --validate .\config\qdisk.catalog.pnb.json
```

Exportar no formato publicado (para fora do repositório):

```powershell
python .\QDisk_Adm\catalog_admin.py --export-package .\config\qdisk.catalog.tools.json "$env:TEMP\qdisk.catalog.tools.json"
```

Gerar o manifesto de uma ISO (grava em `config\manifests\` e imprime o resumo):

```powershell
python .\QDisk_Adm\catalog_admin.py --generate-manifest "$HOME\Downloads\qdisk\PNB-Win11-x64.iso"
```

Preencher os manifestos que faltam num catálogo, com as imagens em `~/Downloads/qdisk`:

```powershell
python .\QDisk_Adm\catalog_admin.py --generate-missing-manifests .\config\qdisk.catalog.pnb.json
python .\QDisk_Adm\catalog_admin.py --generate-missing-manifests .\config\qdisk.catalog.pnb.json --image-dir "D:\isos" --refresh
```

Comportamento de `--generate-missing-manifests`, item a item:

- a imagem local é localizada em `--image-dir` (arquivos `.tibx`, `.iso`, `.img`) pelo `fileName`, depois pelo
  tamanho, pelo SHA-256 entre os de mesmo tamanho e por partes do `name`; sem achar, imprime `PENDENTE`;
- item sem `usbIdentification`: usa o manifesto que já existir em `--out` com o nome da imagem, ou gera um; `OK`;
- item com `usbIdentification` em modo `logical` sem `manifestSha256`: preenche o `manifestSha256` se o manifesto
  estiver em `--out`; senão `SKIP`;
- com `--refresh`, itens `.iso` com `usbIdentification`: reanalisa o manifesto existente (mesma lista de arquivos)
  ou gera um novo; cada imagem é analisada uma vez por execução, mesmo que vários itens apontem para ela;
- o catálogo só é regravado se algum item mudou, mantendo o formato (array ou `{build, items}`).

Sugestões de método de gravação:

```powershell
python .\QDisk_Adm\catalog_admin.py --suggest-write-policies .\config\qdisk.catalog.pnb.json
python .\QDisk_Adm\catalog_admin.py --suggest-write-policies .\config\qdisk.catalog.pnb.json --apply
python .\QDisk_Adm\catalog_admin.py --suggest-write-policies .\config\qdisk.catalog.pnb.json --apply --overwrite
```

Cada item sai como `SKIP` (não é ISO, ou já tem política sem `--overwrite`), `PENDENTE` (sem análise completa ou sem
método que preserve a imagem), `SUGESTAO` (sem `--apply`) ou `OK` (gravado).

Verificar uma ISO no Defender e gravar o resultado nos dois itens que usam a mesma imagem:

```powershell
python .\QDisk_Adm\catalog_admin.py --scan-iso "$HOME\Downloads\qdisk\Quallit_Boot.iso" --catalog .\config\qdisk.catalog.tools.json --item "quallit-boot-sharepoint,quallit-boot-dropbox"
```

Sem `--catalog`/`--item`, só imprime o resultado.

Gerar a ISO limpa:

```powershell
python .\QDisk_Adm\catalog_admin.py --clean-iso "$HOME\Downloads\qdisk\Quallit_Boot.iso" --clean-out "$HOME\Downloads\qdisk\Quallit_Boot_clean.iso" --exclude "Programs\ProduKey\ProduKey.exe"
```

Imprime o caminho, o tamanho e o SHA-256 da ISO gerada.

### Helper de manifesto direto

O QDisk_Adm chama o helper assim (a análise completa só sai por ele):

```powershell
.\dist\ADM\Quallit.IsoManifestTool.exe --iso "<iso>" --out .\config\manifests --manifest-url-base "https://installer.quallit.com.br/qdisk/manifests" --summary-out "<resumo.json>" --quiet
```

Opções do helper: `--iso`, `--out`, `--manifest-url-base`, `--summary-out`, `--reanalyze <manifesto>` (em lugar de
`--out`, refaz só a análise de um manifesto existente), `--quiet`, `--help`.
