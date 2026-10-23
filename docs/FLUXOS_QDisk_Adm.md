# Fluxos do administrador (QDisk_Adm)

Fluxos do QDisk_Adm (`QDisk_Adm/catalog_admin.py`), a ferramenta que mantém os catálogos `config/qdisk.catalog.*.json`,
gera os manifestos USB em `config/manifests/`, decide o método de gravação de cada ISO, verifica as imagens no
Microsoft Defender e publica. Uso botão a botão e linha de comando: [`QDisk_Adm/README.md`](../QDisk_Adm/README.md).
Fluxos do app: [`FLUXOS_APP.md`](FLUXOS_APP.md).

## Sumário

1. [Visão geral](#1-visão-geral)
2. [Início e pastas](#2-início-e-pastas)
3. [Editar e salvar um catálogo](#3-editar-e-salvar-um-catálogo)
4. [Cadastrar uma imagem TIBX](#4-cadastrar-uma-imagem-tibx)
5. [Preparar uma ISO](#5-preparar-uma-iso)
6. [Decidir o método de gravação](#6-decidir-o-método-de-gravação)
7. [Defender e ISO limpa](#7-defender-e-iso-limpa)
8. [Publicar (Upload)](#8-publicar-upload)
9. [Configurações](#9-configurações)
10. [Build completo](#10-build-completo)
11. [Linha de comando em lote](#11-linha-de-comando-em-lote)

## 1. Visão geral

```mermaid
flowchart LR
    A["Abrir: run_qdisk_adm.cmd,<br/>python catalog_admin.py<br/>ou dist/ADM/QDisk_Adm.exe"] --> B["Abre o primeiro<br/>qdisk.catalog.*.json da pasta"]
    B --> C[Editar itens]
    C --> D["TIBX: Hashes…"]
    C --> E["ISO: Preparar ISO…"]
    D --> F[PRONTO PARA PUBLICAR]
    E --> F
    F --> G[Atualizar item, Validar, Salvar]
    G --> H[Upload]
    B --> I[Configurações]
    B --> J[Build completo]
```

## 2. Início e pastas

```mermaid
flowchart TD
    A[Início] --> B{QDISK_ROOT definida?}
    B -- sim --> R[Raiz = QDISK_ROOT]
    B -- não --> C{Executável?}
    C -- "não (Python)" --> R1["Raiz = pasta acima de QDisk_Adm"]
    C -- "sim, em dist/ADM" --> R2[Raiz = dois níveis acima]
    C -- "sim, em outra pasta" --> R3[Raiz = pasta acima da do .exe]
    R & R1 & R2 & R3 --> D{QDISK_CONFIG_DIR definida?}
    D -- sim --> E[Pasta dos catálogos = QDISK_CONFIG_DIR]
    D -- não --> F["Pasta dos catálogos = raiz/config"]
    E & F --> G["Lista qdisk.catalog.*.json e abre o primeiro<br/>em ordem alfabética"]
    G --> H["Logs: pasta do programa/log<br/>e pasta do programa/log_debug"]
```

Da pasta dos catálogos saem também `qdisk.endpoints.json`, `qdisk.tokens.json` e `manifests/`. A caixa
**Catálogo** mostra cada arquivo pela identificação: `devteam`, `devteam2`, `pnb`, `rd`, `tools`.

## 3. Editar e salvar um catálogo

```mermaid
flowchart TD
    A[Catálogo aberto] --> B{Ação na lista}
    B -- Adicionar --> C[Valida só o item do painel<br/>e acrescenta no fim]
    B -- Selecionar item --> D[Painel carrega o item]
    D --> E[Editar campos]
    E --> F{Atualizar item}
    F -- válido --> G[Item trocado na lista, em memória]
    F -- inválido --> X[Mostra os erros]
    B -- Duplicar --> H["Cópia completa abaixo,<br/>nome name-copy"]
    B -- Ativar/desativar --> I[Inverte enabled]
    B -- Remover --> J[Confirma e remove]
    C & G & H & I & J --> K["Catálogo marcado como alterado (* no título)"]
    K --> L[Salvar]
    L --> M{Validação do catálogo}
    M -- ok --> N{Formato build/items marcado?}
    N -- não --> O["Grava array direto"]
    N -- sim --> P["Grava build + items<br/>(product Q-DISK, versão, data)"]
    M -- erro --> X
```

- Trocar de item, **Validar**, **Salvar**, **Upload**, **Preparar ISO…** e **Verificar no Defender** aplicam antes
  as alterações pendentes do painel; se não validarem, a ação para.
- Item inativo é gravado com `enabled: false` e continua no arquivo.
- Só `qdisk.catalog.devteam.json` pode ser salvo sem itens.
- Ao fechar com alterações: salvar e fechar, descartar e fechar, ou voltar. Com qualquer operação em andamento,
  a janela não fecha.

## 4. Cadastrar uma imagem TIBX

```mermaid
flowchart TD
    A[Adicionar ou selecionar item] --> B["Hashes…"]
    B --> C[Escolhe o arquivo]
    C --> D{.tibx ou .iso?}
    D -- não --> E[Pede confirmação]
    D -- sim --> F
    E -- sim --> F[Calcula MD5, SHA-256 e tamanho]
    F --> G["Preenche fileName, hashes e Bytes;<br/>sugere Name se vazio"]
    G --> H[Preencher Group, File URL e Descrição]
    H --> I[Atualizar item]
```

TIBX não leva política de gravação: o painel mostra "Não se aplica: o TIBX é copiado para o USB (NTFS)".

## 5. Preparar uma ISO

```mermaid
flowchart TD
    A["Preparar ISO… com item selecionado"] --> B[Escolhe a ISO uma vez]
    B --> C{Helper Quallit.IsoManifestTool}
    C -- ".exe na pasta do programa<br/>ou em dist/ADM" --> D[Executa o helper]
    C -- "só o projeto .csproj" --> D2[dotnet run do projeto]
    C -- nenhum --> E["Análise parcial em Python<br/>(monta a ISO; python-fallback)"]
    D & D2 --> OK{Sucesso?}
    OK -- não --> X[Mostra o erro e para]
    OK -- sim --> F
    E --> F["manifests/nome.manifest.json<br/>+ usbIdentification no item"]
    F --> G["MD5, SHA-256 e tamanho do manifesto;<br/>fileName se vazio"]
    G --> H{Política de gravação do item}
    H -- não existe --> I[Sugere pela análise]
    H -- de outra imagem --> J[Sugere de novo,<br/>mantendo o rótulo do pendrive]
    H -- desta imagem --> K[Mantém]
    I & J & K --> L[Verificação no Defender da mesma ISO]
    L --> M["PRONTO PARA PUBLICAR ✓/✗"]
    M --> N["Revisar em Editar… e salvar"]
```

- O `manifestUrl` gravado no item é `<primary.baseUrl>/manifests/<nome>.manifest.json`, com `primary.baseUrl` de
  `qdisk.endpoints.json`.
- O helper tem até `QUALLIT_ISO_MANIFEST_HELPER_TIMEOUT_SECONDS` (padrão 600 s) para terminar.
- A análise parcial em Python não gera sugestão de gravação.

## 6. Decidir o método de gravação

```mermaid
flowchart TD
    A["Editar… (janela Gravação USB)"] --> B{Sugerir pela análise}
    B --> C{Análise do manifesto}
    C -- ausente --> X1[Gere o manifesto primeiro]
    C -- "parcial (python-fallback)" --> X2[Gere de novo com o helper<br/>ou defina manualmente]
    C -- "completa (core-1)" --> D{Recomendado preserva a imagem?}
    D -- não --> X3[Sem sugestão]
    D -- sim --> E["Preferido = recomendado;<br/>Permitidos = viáveis sem alterar a imagem;<br/>alvos de boot bios/uefi"]
    E --> F[Ajustes manuais: preferido, permitidos,<br/>alterar a imagem, motivo, rótulo]
    F --> G[Concluir]
    G --> H[Atualizar item]
    H --> I{Validação da política}
    I -- ok --> J[usbWritePolicy no item]
    I -- erro --> X4[Mostra os erros]
```

Métodos: `logical-fat32` (Cópia lógica FAT32), `uefi-ntfs` (UEFI:NTFS), `raw` (RAW/DD) e `fat32-split-wim` (FAT32
com split do WIM; altera a imagem e exige "Permitir alterar a imagem"). O **Rótulo do pendrive** aceita só letras,
dígitos, `_` e `-`; até 11 caracteres com método FAT32 permitido, até 32 nos demais; não se aplica a política só
`raw`. A política guarda o SHA-256 da imagem: se a imagem do item mudar, a coluna **Gravação** mostra
`(outra imagem)` e a validação pede para refazer.

## 7. Defender e ISO limpa

```mermaid
flowchart TD
    A[Verificar no Defender] --> B[SHA-256 da ISO]
    B --> C[Monta a ISO]
    C --> D["MpCmdRun -Scan -ScanType 3<br/>-DisableRemediation"]
    D --> E["imageScan no item: ameaças, Unknown,<br/>.exe com nome de ferramenta do Windows"]
    E --> F{Ameaças?}
    F -- não --> G[PRONTO PARA PUBLICAR: Defender sem ameaças]
    F -- sim --> H["Gerar ISO limpa…"]
    H --> I[Marca arquivos a remover:<br/>ameaças já marcadas]
    I --> J[Escolhe a ISO original e o destino]
    J --> K["robocopy sem os marcados<br/>+ oscdimg: UDF, mesmo rótulo,<br/>boot BIOS e/ou UEFI"]
    K --> L[Mostra caminho e SHA-256]
    L --> M["Publicar a ISO limpa, trocar File URL<br/>e rodar Preparar ISO… com ela"]
```

A ISO limpa exige `oscdimg.exe` (Windows ADK) e, na ISO, `boot\etfsboot.com` e/ou
`efi\microsoft\boot\efisys.bin`.

## 8. Publicar (Upload)

```mermaid
flowchart TD
    A["Upload (Destino: Both, InstallerGit ou Fallback)"] --> B{Catálogo salvo em arquivo?}
    B -- não --> X1[Salve antes]
    B -- sim --> C[Aplica alterações do painel e valida]
    C --> D{Item ativo com ameaça<br/>na última verificação?}
    D -- sim --> E[Confirma publicar mesmo assim]
    D -- não --> F
    E --> F{Alterações não salvas?}
    F -- sim --> G["Salvar e enviar, enviar o do disco ou cancelar"]
    F -- não --> H
    G --> H[Confirma arquivo e destino]
    H --> I["Copia os JSON para raiz/config<br/>se QDISK_CONFIG_DIR for outra pasta"]
    I --> J[publish_catalogs.ps1 -CatalogPath -Target]
    J --> K{"Catálogo oficial?<br/>qdisk.catalog.pnb/rd/tools/devteam.json"}
    K -- não --> X2[Catalogo nao oficial para upload automatico]
    K -- sim --> L["Catálogo + todos os manifests/*.manifest.json"]
    L --> M["Fallback: pscp ou scp para user@host:remotePath;<br/>confere por HTTP em fallback.publicBaseUrl"]
    L --> N["InstallerGit: clone, copia para targetSubdir,<br/>commit e push; confere em primary.baseUrl<br/>(até 5 tentativas, 10 s)"]
    M & N --> O["Resumo na tela; saída completa no log_debug"]
```

`qdisk.catalog.devteam2.json` aparece na caixa **Catálogo** e pode ser editado, mas não é um dos nomes aceitos pelo
script de publicação. Credenciais e destinos: variáveis `QUALLIT_UPDATE_*`, `QUALLIT_PUBLISH_*` e
`QUALLIT_INSTALLER_*`, depois `config/qdisk.endpoints.json`; senha SSH também em `SshPassword` de
`config/secret.md`.

## 9. Configurações

```mermaid
flowchart TD
    A[Configurações] --> B[Aba Endpoints]
    A --> C[Aba Tokens]
    B --> D["URL principal, fallback SSH/HTTPS,<br/>repositório e subpasta do installer"]
    C --> E["RD, PNB, DevTeam: nome, ID de 6 dígitos,<br/>catálogo .json; perfil e data base do downloader"]
    D & E --> F{Salvar}
    F -- inválido --> X[Mostra o erro]
    F -- ok --> G[Grava qdisk.endpoints.json e qdisk.tokens.json]
    G --> H{Tokens mudaram?}
    H -- sim --> I["generate_token_verifiers.py<br/>→ config/qdisk.tokens.app.json"]
    H -- não --> J[Fim]
    I --> J
```

Catálogos associados padrão: `qdisk.catalog.rd.json` (RD), `qdisk.catalog.pnb.json` (PNB) e
`qdisk.catalog.devteam.json` (DevTeam). **Restaurar padrões** só preenche os valores padrão do código; nada é
gravado sem **Salvar**.

## 10. Build completo

```mermaid
flowchart TD
    A[Build completo] --> B[Executar build completo]
    B --> C[Confirma]
    C --> D["Copia os JSON para raiz/config<br/>se QDISK_CONFIG_DIR for outra pasta"]
    D --> E["cmd.exe /c tools/utils/Build-OneFile-Sign_OV.bat<br/>→ python tools/build_release_OV.py"]
    E --> F["Console ao vivo<br/>+ log_debug/build_console_*.log"]
    F --> G{Terminou}
    G --> H["Resumo: código de saída, versão nova,<br/>Git installer, fallback, Git projeto"]
    F -- Cancelar build --> K["taskkill /T /F"]
```

Etapas do pipeline: [`DOCUMENTACAO_TECNICA.md`](DOCUMENTACAO_TECNICA.md#pipeline).

## 11. Linha de comando em lote

```mermaid
flowchart TD
    A["python QDisk_Adm/catalog_admin.py"] --> B{Opção}
    B -- "--validate" --> V[Valida e sai com 0 ou 1]
    B -- "--export-package" --> P["Valida e grava {build, items}"]
    B -- "--generate-manifest" --> M[Manifesto de uma ISO + resumo]
    B -- "--generate-missing-manifests" --> MM["Para cada item: acha a imagem em --image-dir,<br/>gera ou reaproveita o manifesto em --out;<br/>--refresh reanalisa as ISOs com usbIdentification"]
    B -- "--scan-iso" --> S["Defender; com --catalog e --item<br/>grava imageScan"]
    B -- "--clean-iso" --> CL["ISO sem os --exclude em --clean-out"]
    B -- "--suggest-write-policies" --> W["Mostra sugestões;<br/>--apply grava; --overwrite substitui"]
    B -- nenhuma --> GUI[Abre a janela]
```

Uma ação por execução, na ordem do diagrama de cima para baixo. Exemplo de preparo em lote de um catálogo de ISOs:

```powershell
python .\QDisk_Adm\catalog_admin.py --generate-missing-manifests .\config\qdisk.catalog.pnb.json --refresh
python .\QDisk_Adm\catalog_admin.py --suggest-write-policies .\config\qdisk.catalog.pnb.json
python .\QDisk_Adm\catalog_admin.py --suggest-write-policies .\config\qdisk.catalog.pnb.json --apply --overwrite
python .\QDisk_Adm\catalog_admin.py --validate .\config\qdisk.catalog.pnb.json
```

Opções e exemplos completos: [`QDisk_Adm/README.md`](../QDisk_Adm/README.md#linha-de-comando).
