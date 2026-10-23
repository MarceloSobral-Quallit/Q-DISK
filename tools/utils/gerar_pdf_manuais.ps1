# ─────────────────────────────────────────────────────────────────────────────
# qdisk  |  Tools / Utils  |  gerar_pdf_manuais.ps1
# Gera os PDFs do manual do app (assets/manual/manual.html) e do manual de instalacao
# (assets/manual/instalacao.html) pela impressao do Microsoft Edge em modo headless.
# A paginacao vem do @media print de cada HTML.
#
# Uso (PowerShell 7, pwsh): .\tools\utils\gerar_pdf_manuais.ps1 [-OutDir <pasta>] [-Layout v1|v2|v3]
#   Padrao: v3 em docs\manual (Manual_Q-DISK.pdf e Manual_Instalacao_Q-DISK.pdf); v1/v2 com sufixo _v1/_v2.
#
# © Quallit — Licenciado sob GPL-3.0-or-later.
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string] $OutDir = '',
    # v3 (padrao): v2 com capa (versao, data e creditos do rodape do HTML, que sai da ultima pagina); gera
    # <nome>.pdf. v2: diagramacao compacta em A4 (menos espaco em branco), injetada numa copia temporaria do
    # HTML; gera <nome>_v2.pdf. v1: so o @media print do proprio HTML (papel padrao do Edge); gera <nome>_v1.pdf.
    [ValidateSet('v1', 'v2', 'v3')] [string] $Layout = 'v3'
)
$ErrorActionPreference = 'Stop'

# v2 do manual do app: capturas com texto miudo nao cabem lado a lado; ficam menores (texto e imagem
# continuam juntos, pelo @media print do HTML), o que reduz o vazio quando a imagem pula de pagina.
$V2Manual = @'
<style>
@page { size: A4; }
@media print {
  section img { width: auto; max-width: 100%; max-height: 88mm; margin: 6px auto; }
  h3 { margin-top: 10px; }
  p { margin: 6px 0; }
  footer { break-inside: avoid; margin-top: 12px; }
}
</style>
'@
# v2 do manual de instalacao: cada passo vira uma linha de grade, texto a esquerda e janela a direita;
# a linha nao se divide, mas o texto solto entre os passos flui livremente.
$V2Instalacao = @'
<style>
@page { size: A4; }
@media print {
  p:has(+ img), p:has(+ table), table:has(+ img) { break-after: auto; break-inside: auto; }
  .passo { display: grid; grid-template-columns: 1fr 48%; gap: 14px; align-items: start; margin: 8px 0; break-inside: avoid; }
  .passo > img { max-width: 100%; max-height: 92mm; width: auto; margin: 0; justify-self: end; }
  .passo > div > p:first-child { margin-top: 0; }
}
</style>
<script>
document.addEventListener('DOMContentLoaded', function () {
  document.querySelectorAll('section img').forEach(function (img) {
    var texts = [], el = img.previousElementSibling;
    while (el && (el.tagName === 'P' || el.tagName === 'TABLE')) { texts.unshift(el); el = el.previousElementSibling; }
    if (!texts.length) return;
    var row = document.createElement('div'), col = document.createElement('div');
    row.className = 'passo';
    img.parentNode.insertBefore(row, texts[0]);
    texts.forEach(function (t) { col.appendChild(t); });
    row.appendChild(col);
    row.appendChild(img);
  });
});
</script>
'@
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot 'docs\manual' }
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
# Capa da v3: titulo e subtitulo de cada manual.
$covers = @{
    'manual.html'     = @{ Title = 'Manual do Q-DISK'; Subtitle = 'Telas do aplicativo e como executar as principais tarefas' }
    'instalacao.html' = @{ Title = 'Manual de Instalação do Q-DISK'; Subtitle = 'Verificação do certificado do instalador e instalação passo a passo' }
}
$V3Style = @'
<style>
@page :first { margin: 0; }
.capa { display: none; }
@media print {
  .capa { display: flex; flex-direction: column; width: 210mm; height: 296mm; overflow: hidden; break-after: page;
          font-family: "Segoe UI", Arial, sans-serif; color: #202d40; }
  .capa .faixa { height: 12mm; background: #1b9ba8; }
  .capa .corpo { flex: 1; padding: 52mm 24mm 0 24mm; }
  .capa .logo { width: 44mm; height: 44mm; display: block; margin: 0 0 14mm 0; border: 0; }
  .capa .marca { font-size: 11pt; letter-spacing: 2px; text-transform: uppercase; color: #1b9ba8; font-weight: 600; }
  .capa h1 { font-size: 30pt; line-height: 1.15; margin: 4mm 0 6mm 0; color: #202d40; }
  .capa .traco { width: 42mm; height: 3mm; background: #fd811f; border-radius: 2mm; margin: 0 0 8mm 0; }
  .capa .sub { font-size: 13pt; color: #4a5563; margin: 0; }
  .capa .dados { padding: 0 24mm 12mm 24mm; font-size: 10pt; color: #4a5563; }
  .capa .dados .versao { font-size: 13pt; color: #202d40; font-weight: 600; margin-bottom: 2mm; }
  .capa .creditos { margin-top: 6mm; font-size: 8.5pt; line-height: 1.45; color: #6b7280; }
  .capa .rodape { height: 18mm; background: #1b9ba8; color: #fff; font-size: 10pt; display: flex; align-items: center;
                  justify-content: space-between; padding: 0 24mm; }
  .capa .rodape span:last-child { color: #fff; font-weight: 600; }
  footer { display: none; }
}
</style>
<script>
// Leva para a capa os creditos do rodape do HTML (sem a versao/data, que a capa ja mostra).
document.addEventListener('DOMContentLoaded', function () {
  var footer = document.querySelector('footer'), dest = document.querySelector('.capa .creditos');
  if (!footer || !dest) return;
  footer.querySelectorAll(':scope > div:not(.meta)').forEach(function (d) {
    if (d.querySelector('a')) return;
    var p = document.createElement('div'); p.textContent = d.textContent.trim(); dest.appendChild(p);
  });
});
</script>
'@
$version = ([regex]::Match((Get-Content (Join-Path $RepoRoot 'Directory.Build.props') -Raw), '<Version>(.*?)</Version>')).Groups[1].Value
$logoUri = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $RepoRoot 'assets\logo-quallit-512x512.png')))
function Get-CoverHtml([string] $html) {
    $c = $covers[$html]
    $date = (Get-Date).ToString('dd/MM/yyyy')
    @"
<div class="capa">
  <div class="faixa"></div>
  <div class="corpo">
    <img class="logo" src="$logoUri" alt="Quallit" />
    <div class="marca">Quallit Soluções em TI</div>
    <h1>$($c.Title)</h1>
    <div class="traco"></div>
    <p class="sub">$($c.Subtitle)</p>
  </div>
  <div class="dados">
    <div class="versao">Versão $version</div>
    <div>Gerado em $date</div>
    <div class="creditos"></div>
  </div>
  <div class="rodape"><span>Q-DISK</span><span>www.quallit.com.br</span></div>
</div>
"@
}
foreach ($html in $docs.Keys) {
    $src = Join-Path $RepoRoot "assets\manual\$html"
    $pdfName = $docs[$html]
    $tempHtml = $null
    if ($Layout -ne 'v3') { $pdfName = $pdfName -replace '\.pdf$', "_$Layout.pdf" }
    if ($Layout -ne 'v1') {
        $extra = if ($html -eq 'instalacao.html') { $V2Instalacao } else { $V2Manual }
        if ($Layout -eq 'v3') { $extra += $V3Style }
        $text = [IO.File]::ReadAllText($src, [Text.Encoding]::UTF8)
        $i = $text.LastIndexOf('</head>')
        if ($i -lt 0) { throw "</head> nao encontrado em $src" }
        $text = $text.Insert($i, $extra)
        if ($Layout -eq 'v3') {
            $b = [regex]::Match($text, '<body[^>]*>')
            if (-not $b.Success) { throw "<body> nao encontrado em $src" }
            $text = $text.Insert($b.Index + $b.Length, (Get-CoverHtml $html))
        }
        $tempHtml = Join-Path ([IO.Path]::GetTempPath()) "qdisk-pdf-$([guid]::NewGuid().ToString('N'))-$html"
        [IO.File]::WriteAllText($tempHtml, $text, (New-Object Text.UTF8Encoding($false)))
        $src = $tempHtml
    }
    $pdf = Join-Path $OutDir $pdfName
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
    if ($tempHtml) { Remove-Item $tempHtml -Force -ErrorAction SilentlyContinue }
    if (-not (Test-Path $pdf) -or (Get-Item $pdf).Length -eq 0) { throw "PDF nao gerado: $pdf" }
    '{0} -> {1} ({2} KB)' -f $html, $pdf, [math]::Round((Get-Item $pdf).Length / 1KB)
}
