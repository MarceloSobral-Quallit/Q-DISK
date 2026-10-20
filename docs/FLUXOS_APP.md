# Fluxos do qdisk (app)

Como o aplicativo funciona, tela a tela, tirado do código (versão 3.11.10.26, 18/10/2026). Cada fluxo cita onde está
no código. Os diagramas usam Mermaid (renderizados pelo GitHub e pelo VS Code). Fluxos do administrador de catálogos:
[`FLUXOS_GUI_ADM.md`](FLUXOS_GUI_ADM.md). Guia técnico: [`DOCUMENTACAO_TECNICA.md`](DOCUMENTACAO_TECNICA.md).

Abreviações de caminho: `UI/` = `src/qdisk.UI/`, `Core/` = `src/qdisk.Core/`,
`Infra/` = `src/qdisk.Infrastructure/`, `USB` = `UI/Views/UsbBootView.vb`.

## 1. Visão geral

```mermaid
flowchart LR
    A[Abrir o app] --> B{Outra instância aberta?}
    B -- sim --> B1[Pergunta se fecha a outra] --> C
    B -- não --> C[Extrai recursos embutidos<br/>se o build mudou]
    C --> D[Token do dia]
    D --> E[Tela principal]
    E --> F[Downloads]
    E --> G[Copiar Backup TIBX]
    E --> H[Gravar ISO Bootável]
    E --> I[Teste de velocidade]
    E --> J[Logs / Configurações / Sobre / Manual]
    E -. ao abrir .-> K[Verifica atualização do app]
    E -. ao abrir .-> K2[Avisa imagens com versão nova no catálogo]
    E -- Alterar Perfil --> D
```

## 2. Abertura e autenticação

`UI/Program.vb` (`Main`), `UI/Views/TokenPromptForm.vb`, `Core/Services/TokenValidationService.vb`,
`Core/Services/TokenVerifiers.vb`.

```mermaid
flowchart TD
    S[Main] --> M{Mutex Local\qdisk.SingleInstance}
    M -- ocupado --> Q{Fechar a outra instância?}
    Q -- não --> X[Sai]
    Q -- sim --> K[Encerra a outra e espera o mutex] --> E
    M -- livre --> E[EnsureAssetsExtracted:<br/>app_version.txt = versão + MVID?]
    E -- diferente ou faltando arquivo --> W[Extrai _embedded\ para %LOCALAPPDATA%\qdisk<br/>catálogos, manifesto embutido, assets, manual]
    E -- igual --> L
    W --> L[Apaga o qdisk.tokens.json antigo de %LOCALAPPDATA%]
    L --> T{--token na linha de comando?}
    T -- sim --> V
    T -- não --> P[Tela Autenticação] --> V{Bloqueio de tentativas ativo?}
    V -- sim --> A[Aguarde N s/min] --> P
    V -- não --> R{Token confere com algum verificador do dia?<br/>PBKDF2 em qdisk.tokens.app.json}
    R -- sim --> OK[Perfil = o do verificador<br/>RD, PNB ou DevTeam] --> MF[Tela principal]
    R -- verificadores vencidos --> U[Atualização necessária]
    R -- não --> F[Conta a falha] --> P
    MF -- Alterar Perfil --> P
```

- O app não guarda os IDs internos dos perfis: só os verificadores PBKDF2-SHA256 por dia, gerados no build
  (`tools/utils/generate_token_verifiers.py`). O perfil sai do verificador que confere; não há escolha manual.
- Limite: 3 erros livres, depois espera de 30 s que dobra a cada erro, até 15 min; vale entre aberturas do app
  (`config\token_attempts.dat`). Token certo zera a contagem.
- Elevação: o app abre sempre sem elevação (`asInvoker`). "Ativar Modo Administrador" reabre o exe com `runas` e o
  token na linha de comando (`USB` `OnRestartAsAdminClick`, `Core/TokenManager.vb`).

## 3. Atualização do app

`UI/Services/AppUpdateHelper.vb`, `Infra/UpdateClient.vb`, `Core/Services/AuthenticodeVerifier.vb`.

```mermaid
flowchart TD
    A[Abertura com 'Verificar atualizações do aplicativo' ligado<br/>ou botão em Configurações] --> G{Já existe verificação em andamento?}
    G -- sim --> I[Ignora; no botão avisa]
    G -- não --> B[Baixa qdisk.update.json<br/>installer.quallit.com.br, senão mateweb]
    B -- falha ou versão atual --> Z[Fim; mensagem só na verificação manual]
    B -- versão nova --> D{Baixar e instalar / Apenas baixar / Depois / Cancelar}
    D -- instalar ou baixar --> C[Baixa o instalador]
    C --> V{SHA-256 do JSON confere<br/>e assinatura Authenticode de um certificado confiável?}
    V -- não --> R[Apaga o arquivo e avisa]
    V -- sim, instalar --> S[Instalador silencioso, reabre o app, fecha este]
    V -- sim, só baixar --> K[Mantém na pasta escolhida]
```

## 4. Catálogos

`Core/Services/CatalogService.vb`, `Core/Services/ManifestSources.vb`.

- Arquivos em `%LOCALAPPDATA%\qdisk\Data\`: `qdisk.{rd,pnb,devteam,tools}.json`.
  Cada build novo do app sobrescreve essa pasta com as cópias embutidas (as de `config/` no momento do build).
- Downloads e Teste de velocidade usam o catálogo do perfil + `tools`. As telas de USB usam os quatro.
- Atualização remota: Downloads ao abrir (sem forçar) e em "Atualizar" (forçado, com `?refresh=`), Teste de
  velocidade ao iniciar. Tenta o principal e depois o mateweb; valida o JSON; se não mudou, não regrava; falhou,
  fica o local.
- Validação: `name`, `group`, `fileUrl` HTTPS, MD5/SHA-256 no formato, `imageSizeBytes` com SHA-256, extensão
  `.tibx`/`.iso`/`.img`, `usbIdentification` coerente. Itens com `enabled: false` não aparecem.
- Manifestos USB: `manifestUrl` do item; se for do servidor principal, também o mesmo caminho no mateweb.

## 5. Downloads

`UI/Views/DownloadsView.vb`, `Core/Services/DownloadService.vb`, `Core/Services/SegmentedDownloadService.vb`,
`Core/Services/SlowConnectionPolicy.vb`.

```mermaid
flowchart TD
    A[Baixar] --> B{Download multi-conexão ligado?}
    B -- sim --> C{Dropbox ou SharePoint, Range aceito,<br/>tamanho conhecido e >= 20 MB?}
    C -- sim --> D[2 a 8 conexões em fragmentos<br/>redivisão da cauda]
    C -- não --> E[Conexão única]
    B -- não --> E
    D -- fragmento falhou de vez --> F{Continuar com conexão única?}
    F -- não --> X[Cancela]
    F -- sim --> E
    D & E --> G[Grava em arquivo.part]
    G --> H{SHA-256 do catálogo confere?<br/>sem SHA: tamanho}
    H -- não --> Y[Apaga o .part; a imagem anterior fica]
    H -- sim --> I[Troca o arquivo e registra em download_status.json]
```

- SharePoint: conexão abaixo de 1 MiB/s numa janela de 3 s (janela dobra até 60 s) é trocada por outra a partir do
  byte seguinte; no máximo 1 troca por segundo; nenhuma com menos de 16 MiB faltando; respeita `Retry-After`.
- Requisição inicial com novas tentativas em 404/408/429/5xx (3 vezes, 5 s e 15 s).
- "Validar integridade": SHA-256 do catálogo; sem ele, MD5.
- Imagem que já está na pasta (`DownloadOverwriteCheck`, pelo `download_status.json`, sem reler o arquivo): se o
  SHA-256 registrado é o do catálogo, avisa que já está baixada e conferida (padrão: Não); se o catálogo tem SHA-256
  diferente, avisa que há versão nova; sem verificação registrada, pergunta se substitui. A dica da linha mostra
  "Versão nova no catálogo: baixe de novo." quando é o caso.

## 6. Copiar Backup TIBX

`USB` `OnCopyClick`, `Core/Services/UsbDiskLayout.vb` (`TibxVolumeCheck`, `DiskpartScripts`, `UsbVolumeLabels`).

```mermaid
flowchart TD
    A[Copiar TIBX para USB] --> B{USB sem letra, ou volume não serve?<br/>CDFS/UDF de RAW, não NTFS,<br/>partição pequena sem espaço}
    B -- sim --> C{Modo Administrador?}
    C -- não --> N[Pede o modo]
    C -- sim --> D[Explica o motivo e confirma] --> E[diskpart: clean, MBR,<br/>NTFS rótulo QDisk_TIBX]
    B -- não --> F
    E --> F{TIBX já existe no USB<br/>e conferência ligada?}
    F -- igual --> K[Não copia de novo]
    F -- não --> G[Copia com progresso]
    G --> H{Validar hash após cópia TIBX?}
    H -- sim --> I[SHA-256 origem x USB]
```

"Verificar TIBX no USB": escolhe o arquivo no USB e compara tamanho e SHA-256 com o do cache.

## 7. Gravar ISO Bootável

`USB` `OnBootClick`, `BuildBootMethodPlanAsync`, `SelectUsbBootCreationMethod`; Core: `UsbWritePolicyResolver`,
`UsbWriteMethodAdvisor`, `Fat32Layout`, `RawImageTools`, `UsbVerification`.

### 7.1 Escolha do método

```mermaid
flowchart TD
    A[Gravar ISO Bootável] --> B[Acha o item do catálogo:<br/>nome do download, senão arquivo + tamanho]
    B --> C{usbWritePolicy válida para esta ISO?<br/>mesmo SHA-256 e tamanho, métodos conhecidos}
    C -- sim --> D[Métodos permitidos e preferido do catálogo]
    C -- não --> E[Análise local da ISO: El Torito, bootmgr, EFI,<br/>Syslinux/GRUB, ISO híbrida, arquivo > 4 GB]
    D & E --> F[Janela Criar USB Bootavel:<br/>Boot BIOS / Boot UEFI / Boot RAW/DD / Boot UEFI:NTFS<br/>só os permitidos habilitados]
    F --> G{Método}
    G -- BIOS ou UEFI --> L[7.2 Cópia FAT32]
    G -- RAW/DD --> R[7.3 RAW/DD]
    G -- UEFI:NTFS --> U[7.4 UEFI:NTFS]
```

### 7.2 Cópia lógica FAT32 (BIOS e/ou UEFI)

```mermaid
flowchart TD
    A[Confirma; SHA-256 da ISO antes, se ligado] --> B[Desmonta, limpa e formata FAT32 nativo<br/>Fat32Layout: MBR por último]
    B --> C[Rótulo: catálogo > rótulo da ISO > QDisk_+arquivo, 11 caracteres]
    C --> D[Copia os arquivos da ISO]
    D --> E[Confere presença e tamanho de cada arquivo<br/>ausentes são cruzados com eventos do Defender]
    E --> F{BIOS?}
    F -- sim --> G[Grava MBR/PBR com BOOTMGR e confere setores]
    F -- não --> H[Confere EFI\BOOT\BOOTX64.EFI]
    G & H --> I{Validar hash após gravação?}
    I -- sim --> J[SHA-256 de cada arquivo ISO x USB]
```

### 7.3 RAW/DD

```mermaid
flowchart TD
    A[Confirma; segunda confirmação se a análise não indica RAW] --> B[Desmonta e trava os volumes, limpa o disco]
    B --> C[Grava a imagem: corpo primeiro, o 1º MiB por último]
    C --> D{Validar hash após gravação?}
    D -- sim --> E[Compara imagem x USB byte a byte<br/>ANTES de devolver o disco ao Windows]
    D -- não --> F
    E --> F[Libera; Windows relê as partições]
    F --> L[Tira a letra da partição EFI 0xEF do MBR, se o Windows a montou<br/>espera até 10 s]
    L --> G[Registra no histórico: serial do USB, método RawDd, SHA-256]
```

Tirar a letra da partição EFI é inspirado no Rufus, que depois de gravar imagem só remonta a primeira partição que
não é ESP. Com letra, a ESP fica exposta a escritas do Windows ("System Volume Information"). Não evita a primeira
escrita: na bancada de 18/10/2026 o Windows alterou 54 bytes do volume FAT no segundo entre montar e a letra ser
tirada, por isso a comparação RAW continua tolerando o volume FAT/EFI. Só vale neste computador; outro Windows pode
montá-la de novo.

O rótulo do pendrive é o que vem na imagem.

### 7.4 UEFI:NTFS (somente UEFI; usado na Windows 11)

```mermaid
flowchart TD
    A[Confere a imagem UEFI:NTFS embutida pelo SHA-256] --> B[diskpart: partição 1 ativa até 3 MiB do fim,<br/>partição 2 tipo 0xEF no fim]
    B --> C[Grava uefi-ntfs.img na partição 2]
    C --> D[Formata a 1 em NTFS com o rótulo e atribui letra]
    D --> E[Copia os arquivos da ISO, sem dividir o install.wim]
    E --> F[Confere arquivos, partição 2 byte a byte e BOOTX64.EFI]
```

## 8. Imagem do USB (identificar)

`USB` `OnIdentifyUsbClick`; Core: `ManifestVerifier`, `RawFileManifestVerifier`, `RawImageVerifier`, `UsbBootDetection`.

```mermaid
flowchart TD
    A[Imagem do USB] --> T[Trava as outras operações de USB; Cancelar disponível]
    T --> B{Arquivos sentinela de algum item batem?}
    B -- sim --> C[Manifesto: cache, servidor principal, mateweb, cópia local confiável]
    C --> D[Confere cada arquivo do manifesto]
    D -- sucesso --> OK[Mostra item, modo de boot e confiança]
    D -- não deu sucesso e Modo Administrador --> E[RAW pelo histórico, pelos arquivos do item do sentinela<br/>e pela imagem dele]
    E -- confere --> RAW[USB RAW/DD reconhecido]
    E -- não --> OK
    B -- não, Modo Administrador --> F[RAW pelo histórico, pelos arquivos, depois imagem do catálogo<br/>só .iso/.img com RAW permitido]
    F -- confere --> RAW
    F -- não --> G[Detecta modo de boot e informa inconclusivo]
```

RAW "pelos arquivos" (`TryIdentifyRawByManifestAsync`): lê a ISO direto do disco físico (UDF ou ISO9660, leituras
alinhadas a 64 KiB), confere as sentinelas e depois o SHA-256 de cada arquivo do manifesto lógico dos itens `.iso`
com RAW permitido. Não precisa da ISO na máquina nem do histórico. Arquivo divergente só passa (com aviso) se estiver
inteiro dentro da partição FAT/EFI da imagem, que o Windows altera ao montar.

Na comparação RAW com a imagem, a assinatura do disco que o Windows grava no MBR é ignorada; poucas alterações dentro
do volume FAT/EFI da imagem (até 64 KiB, por o Windows montá-lo) passam com aviso; qualquer outra diferença reprova.

## 9. Validar Boot do USB

`USB` `OnVerifyBootClick`, `DetectUsbBootMode` → `UsbBootDetection.Evaluate`.

Ordem: manifesto lógico; sem Modo Administrador, para; RAW pelo histórico, pelos arquivos e pela imagem do catálogo; se a ISO selecionada é
RAW, compara byte a byte; senão avalia os fatos do pendrive:

- UEFI: `EFI\BOOT\BOOTX64.EFI` em volume FAT, ou no NTFS com a partição auxiliar 0xEF idêntica à imagem UEFI:NTFS.
- BIOS: `bootmgr` na raiz, MBR com assinatura e partição ativa, setor de boot do volume referenciando BOOTMGR.

## 10. Zerar USB

`USB` `OnZeroUsbClick`: Modo Administrador; FAT32 até 32 GB, exFAT acima; confirmação; diskpart (clean, MBR,
partição primária, formatação rápida, rótulo `QUALLIT_USB`, letra).

## 11. Proteções das operações de USB

- Uma operação por vez (`TryBeginUsbOperation`); "Imagem do USB" e "Validar Boot" também travam as demais.
- Disco do sistema nunca listado; antes de escrever, confere que é o mesmo disco escolhido (`DiskSafety`).
- Disco não removível ou maior que 256 GB: o técnico digita o número do disco para confirmar.
- Toda função que escreve no disco chama `GuardTargetDisk`.
- "Desativar AutoPlay durante operações USB/ISO": vale para TIBX, cópia FAT32 e UEFI:NTFS.

## 12. Demais telas

- **Teste de velocidade** (`UI/Views/SpeedTestView.vb`, `Core/Services/NetSpeedTestService.vb`): mede Dropbox e
  SharePoint por tempo fixo com 1, 4 e 8 conexões, sem gravar em disco; exporta CSV.
- **Logs** (`UI/Views/LogsView.vb`): `logs\app.log`, linha `data | Usuario | Versao | evento`; abre no fim.
- **Configurações** (`UI/Views/SettingsView.vb`, `Core/Models/AppSettings.vb`): `config\settings.json`.
  "Verificar atualizações de imagens na inicialização" (ligada por padrão, como a verificação do app): ao abrir, atualiza os catálogos do
  perfil e de ferramentas e avisa as imagens baixadas cujo SHA-256 registrado difere do catálogo, oferecendo abrir
  Downloads (`UI/Services/ImageUpdateNotifier.vb`, regra em `Core/Services/ImageUpdateCheck.vb`).
- **Sobre** (`UI/Views/AboutView.vb`) e **Manual** (`UI/Views/ManualView.vb`, `assets\manual\manual.html`).

## 13. Onde ficam os dados (máquina do técnico)

| Caminho | Conteúdo |
|---|---|
| `%LOCALAPPDATA%\qdisk\Data\` | Catálogos |
| `%LOCALAPPDATA%\qdisk\manifests\` | Cache dos manifestos USB |
| `%LOCALAPPDATA%\qdisk\config\settings.json` | Configurações |
| `%LOCALAPPDATA%\qdisk\config\download_status.json` | Downloads e histórico de gravação RAW |
| `%LOCALAPPDATA%\qdisk\config\token_attempts.dat` | Tentativas de token |
| `%LOCALAPPDATA%\qdisk\logs\app.log` | Log |
| `%LOCALAPPDATA%\qdisk\assets\` | Ícones, manual, imagem UEFI:NTFS |
| `%USERPROFILE%\Downloads\qdisk\` | Imagens ISO/TIBX (padrão; ajustável) |
| `%TEMP%\qdisk\updates\` | Instalador baixado pela atualização |
