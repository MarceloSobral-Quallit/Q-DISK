; ─────────────────────────────────────────────────────────────────────────────
; qdisk  |  Installer  |  qdisk.iss
; Instalador per-user (sem elevação UAC), gerado com Inno Setup 6.
;
; Compilar manualmente:
;   iscc tools\installer\qdisk.iss
;
; Compilar pelo pipeline de build (recomendado):
;   python tools\build_release_dotnet_4.py
;   python tools\build_release_dotnet_4.py --no-installer   (pula esta etapa)
;   python tools\build_release_OV.py                        (assinatura OV via signkit)
;
; © Quallit — Licenciado sob GPL-3.0-or-later.
; ─────────────────────────────────────────────────────────────────────────────

; --- Constantes --------------------------------------------------------------
#define AppName      "Q-DISK"
#define AppPublisher "Quallit"
#define AppURL       "https://www.quallit.com.br"
#define AppExeName   "qdisk.exe"

; AppVersion é injetado via /DAppVersion=X.Y.ZZ.ZZ pelo build script.
; Fallback para execução direta (IDE / teste manual):
#ifndef AppVersion
  #define AppVersion "0.0.00.00"
#endif

[Setup]
; ─── IMPORTANTE: NÃO ALTERAR o AppId abaixo. ────────────────────────────────
; Ele identifica o produto para upgrade silencioso e para o desinstalador.
; Qualquer alteração cria entrada duplicada em "Configurações > Aplicativos".
AppId={{569DF4EF-80C4-4B84-BE36-04B8AEB69EC7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}

; Instalação per-user: zero UAC — registro exclusivamente em HKCU.
; O app aparece em Configurações > Aplicativos (Windows 10/11).
PrivilegesRequired=lowest

; Diretório fixo: %LOCALAPPDATA%\qdisk
; (mesma raiz dos dados de runtime: cache, config, logs, assets)
DefaultDirName={localappdata}\qdisk
DisableDirPage=yes

; Atalho no Menu Iniciar dentro da pasta "Quallit"
DefaultGroupName=Quallit
DisableProgramGroupPage=yes

; Ícone do wizard (extraído do projeto)
SetupIconFile=..\..\assets\favicon.ico

; Artefato gerado em dist\
OutputDir=..\..\dist
OutputBaseFilename=Setup_qdisk_{#AppVersion}

; Compressão LZMA2 máxima
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Fecha o executável em uso antes de instalar/atualizar (upgrade silencioso)
CloseApplications=yes
CloseApplicationsFilter=qdisk.exe
RestartApplications=no

; Exibe diálogo de idioma somente quando há mais de um idioma disponível
ShowLanguageDialog=auto

; Assinatura OV (Cloud KMS): só com /DSignKit, passado pelo tools\build_release_OV.py junto com
; /Ssignkit=<comando>. O Inno assina o Setup e o desinstalador. Sem /DSignKit (build v4), nada muda.
#ifdef SignKit
SignTool=signkit
SignedUninstaller=yes
#endif

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en";   MessagesFile: "compiler:Default.isl"

[CustomMessages]
ptbr.OptionalDesktopIcon=Criar ícone na Área de Trabalho
en.OptionalDesktopIcon=Create a Desktop icon

[Tasks]
; Ícone na Área de Trabalho (opcional, desmarcado por padrão)
Name: "desktopicon"; Description: "{cm:OptionalDesktopIcon}"; Flags: unchecked

[Files]
; Executável principal — instala em %LOCALAPPDATA%\qdisk\
Source: "..\..\dist\{#AppExeName}"; DestDir: "{app}"; DestName: "{#AppExeName}"; Flags: ignoreversion

[Icons]
; Atalho no Menu Iniciar (pasta Quallit)
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

; Atalho na Área de Trabalho (criado somente se a tarefa for selecionada)
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; Opção de iniciar o app ao fim da instalação
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove diretórios auto-gerados em runtime (regenerados pelo app no próximo uso).
; Dados do usuário em config\ e logs\ são preservados intencionalmente.
Type: filesandordirs; Name: "{app}\cache"
Type: filesandordirs; Name: "{app}\assets"
Type: filesandordirs; Name: "{app}\Data"
; Remove o diretório raiz somente se estiver vazio após a desinstalação
Type: dirifempty;     Name: "{app}"
