# GUI_ADM - Gerador de catalogos

Aplicativo auxiliar para editar, validar e exportar catalogos do qdisk.

O GUI acompanha a versao principal registrada em `version_info.txt`, exibindo-a no titulo da janela e usando-a nos metadados `build.version` quando exporta ou salva no formato publicado.

No pipeline v3, o ADM tem compilacao propria via PyInstaller e e gerado em `dist/ADM/GUI_ADM.exe`. O executavel recebe a mesma versao do app principal nos metadados `FileVersion` e `ProductVersion`.

Quando executado como `.exe`, o ADM usa os JSONs oficiais em `config/`, igual ao modo Python. A pasta `dist/ADM/` fica reservada ao executavel do ADM e nao recebe copia dos JSONs no build. O build v3 monta `dist/update` e sincroniza os repositorios Git a partir dos JSONs de `config/`.

## Como executar

Executavel gerado pelo build v3:

```powershell
.\dist\ADM\GUI_ADM.exe
```

Execucao em modo desenvolvimento:

```powershell
python .\GUI_ADM\catalog_admin.py
```

Ou use o launcher:

```powershell
.\GUI_ADM\run_gui_adm.cmd
```

## Regra de validacao

Quando a pendencia for comportamento do `GUI_ADM`, a validacao deve ser feita pela interface grafica ou pela CLI oficial deste aplicativo. Scripts externos e comandos diretos podem ajudar no diagnostico, mas nao substituem um teste do fluxo oficial.

Para manifestos, o aceite funcional deve passar pelo botao `Preparar ISO…`, pelo CLI `--generate-manifest` ou pelo CLI `--generate-missing-manifests`. Executar diretamente o helper .NET ou montar ISO manualmente serve apenas como verificacao tecnica auxiliar.

## Fluxo recomendado

1. Abra um arquivo em `config/qdisk.*.json`.
2. `Adicionar` (barra da lista) e preencha `Name`, `Group`, `File URL` e a `Descrição`.
3. **ISO:** clique em `Preparar ISO…` e escolha a ISO uma vez. Em sequencia, o GUI calcula hashes e tamanho, gera o
   manifesto USB pelo helper .NET, sugere a gravacao (refeita se a imagem mudou, mantendo o rotulo escolhido) e
   verifica a imagem no Microsoft Defender. Revise a gravacao em `Editar…` (metodo e `Rótulo do pendrive`).
   **TIBX:** clique em `Hashes…` (ao lado de `Arquivo de imagem`) e escolha o arquivo.
4. Confira a lista `PRONTO PARA PUBLICAR` no fim do painel do item: URL, arquivo, hashes, descricao e, para ISO,
   manifesto com analise completa, gravacao da imagem atual e Defender sem ameacas. Cada linha fica com ✓ ou ✗.
5. `Atualizar item` (rodape do painel), `Validar` e salvar como array direto para manter o arquivo-fonte em `config/`.

Se o helper do manifesto falhar, o erro aparece (o GUI nao cai mais em silencio na analise parcial em Python; ela so
e usada quando o helper nao existe na maquina, e nao gera sugestao de gravacao).

Para tirar uma imagem da lista sem perder dados, selecione o item e use `Ativar/desativar`, ou mude `Status` para `Inativo` e clique em `Atualizar item`. O app grava `enabled: false` somente em itens inativos; itens ativos continuam no formato antigo, sem campo extra, para manter compatibilidade.

Se voce alterar campos do item selecionado e selecionar outro item, o GUI aplica a alteracao apenas em memoria e marca o catalogo como alterado. O salvamento em disco e perguntado somente ao fechar ou enviar upload.

Para gerar o formato publicado com metadados, marque `Salvar no formato publicado com build/items` no menu `Salvar ▾` antes de usar `Salvar como…`.

## Validador sem interface

```powershell
python .\GUI_ADM\catalog_admin.py --validate .\config\qdisk.tools.json
```

## Exportar pacote publicado

```powershell
python .\GUI_ADM\catalog_admin.py --export-package .\config\qdisk.tools.json .\temp\qdisk.tools.json
```

O app aceita os dois formatos suportados pelo qdisk:

- array direto `[...]`, usado como fonte em `config/`;
- objeto `{ "build": ..., "items": [...] }`, usado para publicacao.

## Manifestos de USB logico

USB gravado em modo RAW/DD pode ser verificado por comparacao binaria da imagem com o disco fisico. USB criado por copia logica BIOS/UEFI nao e byte a byte igual a ISO; nesse caso a validacao correta usa manifesto de arquivos.

Pela interface grafica, selecione o item do catalogo que deve receber o manifesto e use o botao `Preparar ISO…`. O GUI monta a ISO, salva o manifesto em `config/manifests/`, aplica o resumo `usbIdentification` ao item selecionado e preenche MD5, SHA-256 e tamanho da imagem a partir do manifesto gerado.

Tambem e possivel gerar um manifesto completo de uma ISO local pelo CLI:

```powershell
python .\GUI_ADM\catalog_admin.py --generate-manifest "C:\caminho\imagem.iso" --out .\config\manifests
```

O comando usa o helper .NET `tools/Quallit.IsoManifestTool` como caminho preferencial. O helper calcula MD5/SHA-256 da imagem, enumera arquivos internos, calcula SHA-256 por arquivo quando permitido pelo sistema, gera `treeHash`, detecta sentinelas e salva `*.manifest.json` em `config/manifests/`. Ao final ele imprime um resumo `usbIdentification` para inserir no item do catalogo.

O helper usa DiscUtils como leitura primaria e faz fallback para montagem ISO do Windows quando DiscUtils retorna uma arvore incompleta. A rotina Python antiga por `Mount-DiskImage` continua como fallback operacional do GUI_ADM caso o helper nao esteja disponivel, retorne erro ou exceda o tempo limite configurado por `QUALLIT_ISO_MANIFEST_HELPER_TIMEOUT_SECONDS`:

```powershell
dotnet run --project .\tools\Quallit.IsoManifestTool\Quallit.IsoManifestTool.csproj -c Release -- --iso "C:\caminho\imagem.iso" --out .\config\manifests --manifest-url-base "https://installer.quallit.com.br/qdisk/manifests" --summary-out .\temp\summary.json
```

O botao `Preparar ISO…`, o CLI `--generate-manifest` e o modo em lote `--generate-missing-manifests` usam a mesma funcao integrada.

Durante o fallback Python, a CLI imprime progresso a cada 250 arquivos processados. Arquivos conhecidos por bloqueio de antivirus podem ser mantidos no manifesto como `unhashedFiles`, com tamanho e motivo, sem abortar a geracao do JSON.

Para gerar manifestos ausentes em lote e atualizar o catalogo com `usbIdentification`:

```powershell
python .\GUI_ADM\catalog_admin.py --generate-missing-manifests .\config\qdisk.pnb.json --image-dir "$HOME\Downloads\qdisk" --out .\config\manifests --manifest-url-base "https://installer.quallit.com.br/qdisk/manifests"
```

O modo em lote procura a ISO por tamanho, SHA-256 e nome, preserva itens que ja possuem `usbIdentification` e reporta pendencias quando a imagem local nao e encontrada.

Use `--manifest-url-base` quando o manifesto precisar apontar para uma URL absoluta ou outro caminho relativo:

```powershell
python .\GUI_ADM\catalog_admin.py --generate-manifest "C:\caminho\imagem.iso" --out .\config\manifests --manifest-url-base "https://installer.quallit.com.br/qdisk/manifests"
```

O GUI preserva `usbIdentification` e campos futuros desconhecidos ao abrir, editar e salvar catalogos. A grade mostra a coluna `USB ID` com `logical`, `raw`, `logical+raw` ou `erro`, e a validacao confere os campos minimos de manifestos logicos e RAW/DD.

## Metodo de gravacao USB (`usbWritePolicy`)

O metodo de gravacao de cada ISO dos catalogos e decidido aqui e publicado no item. O app usa essa decisao; a analise
local do app fica para ISOs fora dos catalogos e para itens sem politica.

```json
"usbWritePolicy": {
  "imageSha256": "<sha256 da imagem analisada>",
  "preferredMethod": "raw",
  "allowedMethods": ["raw"],
  "preserveSourceImage": true,
  "allowContentTransformations": false,
  "note": "motivo da decisao",
  "volumeLabel": "PNB_Win11"
}
```

`volumeLabel` (opcional, campo `Rótulo do pendrive` da janela) e o rotulo do pendrive na copia logica e no UEFI:NTFS:
so letras, digitos, `_` e `-`; ate 11 caracteres quando a politica permite metodo FAT32, ate 32 no NTFS. Recusado em
politica so RAW/DD: a imagem e copiada byte a byte e o pendrive fica com o rotulo da propria ISO. Sem `volumeLabel`, o
app usa o rotulo da ISO e, se a ISO nao tiver, `QDisk_` + nome do arquivo. A copia de TIBX formata com `QDisk_TIBX`.

Metodos: `logical-fat32`, `uefi-ntfs`, `raw` e `fat32-split-wim` (este altera a imagem e so e aceito com
`allowContentTransformations=true`).

A sugestao vem da analise gravada no manifesto pelo helper .NET (`analysisEngine: core-1`), que usa a mesma regra do
app (`UsbWriteMethodAdvisor` no Core): El Torito, bootmgr, EFI, Syslinux, GRUB, Bootwiz, ISO hibrida (MBR/GPT na area
de sistema) e arquivos acima de 4 GB. O fallback Python grava `analysisEngine: python-fallback` e nao gera sugestao.

Pela interface, na secao `GRAVAÇÃO USB` do painel do item (resumo da politica; `Editar…` abre a janela com os controles):

- `Preparar ISO…` ja preenche a sugestao quando o item nao tem politica ou a politica e de outra imagem (revise e salve);
- `Sugerir pela analise` refaz a sugestao a partir da analise do item;
- desmarcar `Definir o método de gravação deste item` e clicar em `Atualizar item` remove a politica.

As escolhas da janela ficam no formulario do item, como os demais campos: so valem depois de `Atualizar item`.

A coluna `Gravação` da grade mostra o metodo preferido, ou `(outra imagem)` quando o `imageSha256` da politica difere
do `sha256` do item. A validacao recusa esse caso ate a politica ser refeita.

Pela CLI:

```powershell
# regera os manifestos das ISOs que ja tem usbIdentification (analise nova)
python .\GUI_ADM\catalog_admin.py --generate-missing-manifests .\config\qdisk.pnb.json --refresh --image-dir "<pasta das ISOs>" --out .\config\manifests --manifest-url-base "https://installer.quallit.com.br/qdisk/manifests"

# mostra as sugestoes; --apply grava; --overwrite substitui politicas existentes
python .\GUI_ADM\catalog_admin.py --suggest-write-policies .\config\qdisk.pnb.json
python .\GUI_ADM\catalog_admin.py --suggest-write-policies .\config\qdisk.pnb.json --apply
```

## Verificacao da imagem no Microsoft Defender

Arquivos da ISO que o Defender barra fazem a gravacao logica falhar na maquina que grava (quarentena durante a copia)
ou ficam bloqueados pelas regras ASR do Intune. Antes de publicar uma ISO:

- `Verificar no Defender` monta a ISO so leitura e roda `MpCmdRun -Scan -ScanType 3 -DisableRemediation` (nada vai para
  a quarentena). O resultado fica no item, campo `imageScan` (ameacas, itens nao classificados, `.exe` com nome de
  ferramenta do Windows — heuristica para a regra ASR "ferramentas do sistema copiadas"), e o resumo aparece abaixo dos
  botoes. Vale para a imagem do `sha256` atual; se a imagem mudar, o resumo pede para refazer.
- `Upload` pede confirmacao quando algum item habilitado tem ameaca na ultima verificacao.
- `Gerar ISO limpa…` lista os arquivos (as ameacas ja marcadas) e gera uma ISO sem eles com `oscdimg` (Windows ADK):
  UDF, mesmo rotulo, boot BIOS (`boot\etfsboot.com`) e/ou UEFI (`efi\microsoft\boot\efisys.bin`). Outro layout de boot
  e recusado. A ISO limpa e outra imagem: subir nas origens, atualizar `Hashes…`, manifesto e politica.

Pela CLI:

```powershell
# varre e grava imageScan nos itens (sem --catalog so mostra)
python .\GUI_ADM\catalog_admin.py --scan-iso "<pasta>\Quallit_Boot.iso" --catalog .\config\qdisk.tools.json --item "quallit-boot-sharepoint,quallit-boot-dropbox"

# ISO sem os arquivos indicados (relativos a raiz da ISO)
python .\GUI_ADM\catalog_admin.py --clean-iso "<pasta>\Quallit_Boot.iso" --clean-out "<pasta>\Quallit_Boot_clean.iso" --exclude "Programs\ProduKey\ProduKey.exe"
```

## Tokens de acesso (Configuracoes)

Os IDs internos dos perfis (RD, PNB, DevTeam) ficam em `config/qdisk.tokens.json`, editado em `Configuracoes`.
O app principal nao leva esses IDs: ele embute `config/qdisk.tokens.app.json`, com verificadores do token de cada
dia. Ao salvar IDs alterados, o `GUI_ADM` regera esse arquivo (`tools/utils/generate_token_verifiers.py`, cerca de
20 s); sem Python disponivel, o proximo build do pipeline regera. Ver `docs/DOCUMENTACAO_TECNICA.md` (§14, regra do token para o site).

## Console e logs

A janela possui uma area `Console` (abaixo da lista; `Limpar` e `Recolher`) com eventos de abertura, salvamento, calculo de hashes, validacao e upload. Avisos e erros aparecem em cor propria.

A mensagem final do upload mostra apenas um resumo do resultado. A saida completa do script permanece no console e no `log_debug`.

Os arquivos ficam em:

- `GUI_ADM/log/catalog_admin.log`: log operacional resumido.
- `GUI_ADM/log_debug/catalog_admin_debug_YYYYMMDD_HHMMSS.log`: log detalhado por execucao, incluindo saida completa do upload.

## Upload de catalogo

Use `Upload` (barra superior, com o `Destino` ao lado) para salvar, validar e enviar o catalogo aberto para os destinos online configurados.

Antes do envio, o GUI aplica alteracoes pendentes do item selecionado em memoria. Se o catalogo ainda nao foi salvo em disco, ele pergunta se deve salvar antes de publicar, evitando enviar uma versao antiga do JSON por engano.

O upload usa o script:

```powershell
.\tools\utils\publish_catalogs.ps1
```

Por padrao, ele publica o catalogo no fallback `mateweb.ddns.net` e no repositorio Git `quallit-installer`, mantendo os mesmos nomes oficiais `qdisk.*.json`.

Quando existirem arquivos `config/manifests/*.manifest.json`, o mesmo script tambem publica esses manifestos em `manifests/`, por exemplo `https://installer.quallit.com.br/qdisk/manifests/<arquivo>.manifest.json`.

Destino e credenciais do envio (`tools/utils/publish_catalogs.ps1`): variaveis `QUALLIT_UPDATE_*`, `QUALLIT_PUBLISH_*` e `QUALLIT_INSTALLER_*`; depois `config/qdisk.endpoints.json`; depois os defaults locais de `tools/utils/publish_update_package.ps1`, se existir. A senha do SSH vem da variavel de ambiente ou de `SshPassword` em `config/secret.md`.

A verificacao do destino `installer.quallit.com.br` usa tentativas com cache-buster, pois o conteudo pode levar alguns segundos para refletir o push do Git.

Para testar sem enviar:

```powershell
.\tools\utils\publish_catalogs.ps1 -CatalogPath .\config\qdisk.tools.json -DryRun
```