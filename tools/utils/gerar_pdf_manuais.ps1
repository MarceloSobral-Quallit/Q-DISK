# ─────────────────────────────────────────────────────────────────────────────
# qdisk  |  Tools / Utils  |  gerar_pdf_manuais.ps1
# Gera os PDFs do manual do app (assets/manual/manual.html) e do manual de instalacao
# (assets/manual/instalacao.html) pela impressao do Microsoft Edge em modo headless.
# A paginacao vem do @media print de cada HTML.
#
# Uso (PowerShell 7, pwsh): .\tools\utils\gerar_pdf_manuais.ps1 [-OutDir <pasta>]
#   Padrao: assets\manual\pdf (Manual_Q-DISK.pdf e Manual_Instalacao_Q-DISK.pdf).
#
# © Quallit — Licenciado sob GPL-3.0-or-later.
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string] $OutDir = ''
)
$ErrorActionPreference = 'Stop'
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'assets\manual\pdf' }
$OutDir = (New-Item -ItemType Directory -Force $OutDir).FullName

$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge (msedge.exe) nao encontrado.' }

$docs = [ordered]@{
    'manual.html'     = 'Manual_Q-DISK.pdf'
    'instalacao.html' = 'Manual_Instalacao_Q-DISK.pdf'
}
foreach ($html in $docs.Keys) {
    $src = Join-Path $RepoRoot "assets\manual\$html"
    $pdf = Join-Path $OutDir $docs[$html]
    if (Test-Path $pdf) { Remove-Item $pdf -Force }
    $uri = ([Uri](Resolve-Path $src).Path).AbsoluteUri
    # Perfil temporario: nao interfere no Edge aberto do usuario.
    $profileDir = Join-Path ([IO.Path]::GetTempPath()) "qdisk-pdf-$([guid]::NewGuid().ToString('N'))"
    & $edge --headless=new --disable-gpu --no-pdf-header-footer "--user-data-dir=$profileDir" "--print-to-pdf=$pdf" $uri 2>$null | Out-Null
    # O msedge.exe pode sair antes do processo que grava o PDF: espera o arquivo existir e parar de crescer.
    $sw = [Diagnostics.Stopwatch]::StartNew(); $last = -1
    while ($sw.Elapsed.TotalSeconds -lt 120) {
        $size = if (Test-Path $pdf) { (Get-Item $pdf).Length } else { -1 }
        if ($size -gt 0 -and $size -eq $last) { break }
        $last = $size
        Start-Sleep -Milliseconds 1000
    }
    Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path $pdf) -or (Get-Item $pdf).Length -eq 0) { throw "PDF nao gerado: $pdf" }
    '{0} -> {1} ({2} KB)' -f $html, $pdf, [math]::Round((Get-Item $pdf).Length / 1KB)
}
