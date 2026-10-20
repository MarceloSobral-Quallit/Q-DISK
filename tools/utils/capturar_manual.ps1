# ─────────────────────────────────────────────────────────────────────────────
# qdisk  |  Tools / Utils  |  capturar_manual.ps1
# Refaz as capturas de tela do manual (assets/manual/images) pelo app, por UI Automation: abre o app SEM Modo
# Administrador (estado descrito no manual), captura a autenticacao, entra com o token do dia do perfil e percorre
# as telas pelo menu lateral. Os nomes e a ordem seguem MANUAL_IMAGE_NAMES de tools/build_release_dotnet_4.py; o
# pipeline embute as imagens no manual.html.
#
# Uso: .\tools\utils\capturar_manual.ps1 -OutDir <pasta> [-TokenProfile RD] [-Exe <qdisk.exe>]
#   Gere numa pasta temporaria, confira e copie para assets\manual\images.
#
# © Quallit — Licenciado sob GPL-3.0-or-later.
# ─────────────────────────────────────────────────────────────────────────────
param(
    [Parameter(Mandatory)] [string] $OutDir,
    [string] $TokenProfile = 'RD',
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string] $Exe = '',
    # Tamanho da janela nas capturas (o do manual atual: 786 x 643 sem a sombra).
    [int] $Width = 786,
    [int] $Height = 643
)
$ErrorActionPreference = 'Stop'
. (Join-Path $RepoRoot 'tools\testes\teste_real\lib.ps1')
$script:OutDir = (New-Item -ItemType Directory -Force $OutDir).FullName
if (-not $Exe) { $Exe = Join-Path $RepoRoot 'src\qdisk.UI\bin\Release\net8.0-windows\win-x64\qdisk.exe' }

function Get-DailyToken([string] $profileName) {
    $ids = (Get-Content (Join-Path $RepoRoot 'config\qdisk.tokens.json') -Raw | ConvertFrom-Json).profiles
    $id = ($ids | Where-Object profile -eq $profileName).internalId
    $days = ((Get-Date).Date - [datetime]'1970-10-01').Days + [int]$id
    $hash = [BitConverter]::ToString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes("$days"))).Replace('-', '').ToLower()
    return ($hash -replace '\D', '').Substring(0, 6)
}

# Janela no tamanho do manual: SetWindowPos inclui as bordas invisiveis; corrige pela diferenca medida pelo DWM.
function Set-ShotSize($win) {
    Focus-Window $win $Width $Height
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    $r = New-Object QW+RECT
    [QW]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null
    $dw = $Width - ($r.R - $r.L); $dh = $Height - ($r.B - $r.T)
    if ($dw -ne 0 -or $dh -ne 0) { Focus-Window $win ($Width + $dw) ($Height + $dh) }
}

function Show-Screen($main, [string] $menu, [string] $file, [int] $settleMs = 2500) {
    Invoke-Button $main $menu
    Start-Sleep -Milliseconds $settleMs
    Save-Shot $main $file | Out-Null
    "capturada: $file ($menu)"
}

Get-Process qdisk -ErrorAction SilentlyContinue | ForEach-Object { $_.Kill(); $_.WaitForExit(5000) | Out-Null }
# explorer.exe abre o app com o token do usuario, sem elevacao, mesmo a partir de um terminal elevado.
Start-Process explorer.exe -ArgumentList "`"$Exe`""
$proc = Wait-Until { Get-Process qdisk -ErrorAction SilentlyContinue | Select-Object -First 1 } 30
if (-not $proc) { throw 'O app nao abriu.' }
try {
    $auth = Wait-Window $proc.Id 'Autentica*' 40
    if (-not $auth) { throw 'Tela de autenticacao nao apareceu.' }
    Start-Sleep 1
    Save-Shot $auth 'autenticacao.png' | Out-Null
    'capturada: autenticacao.png'
    Set-Text $auth (Get-DailyToken $TokenProfile); Invoke-Button $auth 'Validar'

    $main = Wait-Window $proc.Id 'Q-DISK*' 60
    if (-not $main) { throw 'Tela principal nao apareceu.' }
    Set-ShotSize $main
    Start-Sleep 3
    Save-Shot $main 'inicio.png' | Out-Null
    'capturada: inicio.png'
    Show-Screen $main 'Downloads' 'downloads.png' 5000
    Show-Screen $main 'Copiar Backup TIBX' 'copiar-backup-tibx-2.png'
    Show-Screen $main 'Gravar ISO Bootável' 'gravar-iso-bootavel-2.png'
    Show-Screen $main 'Teste de velocidade' 'teste-velocidade.png'
    Show-Screen $main 'Logs' 'logs.png' 3500
    Show-Screen $main 'Configurações' 'configuracoes.png'
    Show-Screen $main 'Sobre' 'sobre.png'
    Show-Screen $main 'Manual' 'manual.png' 3500
}
finally {
    Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; if (-not $_.WaitForExit(5000)) { $_.Kill() } }
}
