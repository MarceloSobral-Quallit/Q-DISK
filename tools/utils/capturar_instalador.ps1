# ─────────────────────────────────────────────────────────────────────────────
# qdisk  |  Tools / Utils  |  capturar_instalador.ps1
# Refaz as capturas do manual de instalacao (assets/manual/instalacao.html) a partir do Setup, por UI Automation
# e Win32: verificacao da assinatura (Propriedades > Assinaturas Digitais > Detalhes > Exibir Certificado),
# aviso do SmartScreen de arquivo baixado e as paginas do instalador. INSTALA o Q-DISK no usuario atual
# (%LOCALAPPDATA%\qdisk, sem UAC); "Iniciar o Q-DISK" e desmarcado no fim.
#
# Uso (PowerShell 7, pwsh): .\tools\utils\capturar_instalador.ps1 -OutDir <pasta> [-Setup <Setup_qdisk_*.exe>]
#   Sem -Setup: o Setup_qdisk_<versao>.exe mais recente de dist\.
#   Confira as imagens e copie para assets\manual\images\instalacao; depois embuta no HTML:
#   .\tools\utils\capturar_instalador.ps1 -EmbedOnly
#
# © Quallit — Licenciado sob GPL-3.0-or-later.
# ─────────────────────────────────────────────────────────────────────────────
param(
    [string] $OutDir = '',
    [string] $Setup = '',
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    # So embute as imagens de assets\manual\images\instalacao no instalacao.html (sem capturar).
    [switch] $EmbedOnly
)
$ErrorActionPreference = 'Stop'
$ManualHtml = Join-Path $RepoRoot 'assets\manual\instalacao.html'
$ImagesDir = Join-Path $RepoRoot 'assets\manual\images\instalacao'

# Cada <img data-src="nome.png" src="..."> do instalacao.html recebe a imagem nome.png em base64.
# O atributo data-src identifica a imagem (o src muda a cada embutimento).
function Update-EmbeddedImages {
    $html = [IO.File]::ReadAllText($ManualHtml, [Text.Encoding]::UTF8)
    $script:missing = @()
    $html = [regex]::Replace($html, '<img\b([^>]*?)\bdata-src="([^"]+)"([^>]*?)\bsrc="[^"]*"', {
        param($m)
        $png = Join-Path $ImagesDir $m.Groups[2].Value
        if (-not (Test-Path $png)) { $script:missing += $m.Groups[2].Value; return $m.Value }
        $uri = 'data:image/png;base64,' + [Convert]::ToBase64String([IO.File]::ReadAllBytes($png))
        return "<img$($m.Groups[1].Value)data-src=`"$($m.Groups[2].Value)`"$($m.Groups[3].Value)src=`"$uri`""
    })
    [IO.File]::WriteAllText($ManualHtml, $html, (New-Object Text.UTF8Encoding($false)))
    if ($script:missing) { "AVISO: imagens nao encontradas em ${ImagesDir}: $($script:missing -join ', ')" }
    "instalacao.html atualizado: $ManualHtml"
}

if ($EmbedOnly) { Update-EmbeddedImages; return }
if (-not $OutDir) { throw 'Informe -OutDir (ou use -EmbedOnly).' }

. (Join-Path $RepoRoot 'tools\utils\uia_lib.ps1')
$script:OutDir = (New-Item -ItemType Directory -Force $OutDir).FullName
if (-not $Setup) {
    $Setup = (Get-ChildItem (Join-Path $RepoRoot 'dist') -Filter 'Setup_qdisk_*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
if (-not $Setup -or -not (Test-Path $Setup)) { throw 'Setup_qdisk_*.exe nao encontrado (rode o pipeline ou informe -Setup).' }

if (-not ('QWI' -as [type])) {
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class QWI {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    public static string Text(IntPtr h) { var sb = new StringBuilder(2048); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
    public static string Cls(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    public static List<IntPtr> TopOf(uint pid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static List<IntPtr> Children(IntPtr parent) {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    // Pagina visivel de uma folha de propriedades (filho #32770 visivel).
    public static string VisiblePage(IntPtr sheet) {
        foreach (var h in Children(sheet)) if (Cls(h) == "#32770" && IsWindowVisible(h)) return Text(h);
        return "";
    }
}
'@
}
$PSM_SETCURSEL = 0x0465
$LVM_SETCOLUMNWIDTH = 0x101E
$WM_CLOSE = 0x0010

function Get-VisibleChild([IntPtr] $win, [string] $class, [string] $textLike = '*') {
    [QWI]::Children($win) | Where-Object { [QWI]::IsWindowVisible($_) -and [QWI]::Cls($_) -eq $class -and ([QWI]::Text($_) -replace '&', '') -like $textLike } | Select-Object -First 1
}
# Os dialogos da shell e do Inno nao aparecem como filhos da raiz no UIA: localiza por titulo via Win32.
function Wait-TopByTitle([string] $title, [int] $timeoutSec = 20) {
    Wait-Until { $h = [QWI]::FindWindow([NullString]::Value, $title); if ($h -ne [IntPtr]::Zero) { $h } } $timeoutSec
}
# Clique real do mouse: BM_CLICK assincrono nao abre os dialogos modais das paginas de propriedades.
function Click-ChildButton([IntPtr] $win, [string] $text) {
    $b = Get-VisibleChild $win 'Button' $text
    if (-not $b) { $b = Get-VisibleChild $win 'TNewButton' $text }
    if (-not $b) { throw "Botao '$text' nao encontrado" }
    [QW]::SetForegroundWindow($win) | Out-Null
    Start-Sleep -Milliseconds 300
    Click-Element ($AE::FromHandle($b))
    Start-Sleep -Milliseconds 800
}
function Select-SheetPage([IntPtr] $sheet, [string] $pageLike) {
    for ($i = 0; $i -lt 10; $i++) {
        [QWI]::SendMessage($sheet, $PSM_SETCURSEL, [IntPtr]$i, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 400
        if ([QWI]::VisiblePage($sheet) -like $pageLike) { Start-Sleep -Milliseconds 500; return }
    }
    throw "Pagina '$pageLike' nao encontrada"
}
function Shot([IntPtr] $win, [string] $file) {
    [QW]::SetForegroundWindow($win) | Out-Null
    Start-Sleep -Milliseconds 700
    Save-Shot ($AE::FromHandle($win)) $file | Out-Null
    "capturada: $file"
}
# Troca, na imagem ja salva, o texto de um controle (ex.: caminho com o nome do usuario) por $replacement.
function Hide-ControlText([IntPtr] $win, [IntPtr] $ctl, [string] $file, [string] $replacement) {
    $wr = New-Object QW+RECT
    [QW]::DwmGetWindowAttribute($win, 9, [ref]$wr, 16) | Out-Null
    $cr = $AE::FromHandle($ctl).Current.BoundingRectangle
    $path = Join-Path $script:OutDir $file
    $bmp = [Drawing.Bitmap]::FromStream([IO.MemoryStream]::new([IO.File]::ReadAllBytes($path)))
    $g = [Drawing.Graphics]::FromImage($bmp)
    $x = [int]$cr.X - $wr.L; $y = [int]$cr.Y - $wr.T
    $bg = $bmp.GetPixel([Math]::Max(0, $x - 2), $y + 2)
    $g.FillRectangle((New-Object Drawing.SolidBrush $bg), $x, $y, [int]$cr.Width, [int]$cr.Height)
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $font = New-Object Drawing.Font('Segoe UI', 9)
    $g.DrawString($replacement, $font, [Drawing.Brushes]::Black, $x - 2, $y)
    $g.Dispose()
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Copia do Setup com a Marca da Web (zona Internet), como o arquivo chega a quem o baixa.
$work = (New-Item -ItemType Directory -Force (Join-Path ([IO.Path]::GetTempPath()) 'qdisk-captura-instalador')).FullName
$name = Split-Path $Setup -Leaf
$copy = Join-Path $work $name
Copy-Item $Setup $copy -Force
Set-Content -LiteralPath $copy -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`nHostUrl=https://installer.quallit.com.br/qdisk/$name"

# 1. Verificacao da assinatura pelas Propriedades do arquivo (a folha vive neste processo).
(New-Object -ComObject Shell.Application).Namespace($work).ParseName($name).InvokeVerb('properties')
$props = Wait-TopByTitle "Propriedades de $([IO.Path]::GetFileNameWithoutExtension($name))"
if (-not $props) { throw 'Janela de Propriedades nao abriu.' }
Focus-Window ($AE::FromHandle($props))
Select-SheetPage $props 'Assinaturas*'
$list = Get-VisibleChild $props 'SysListView32'
[QWI]::SendMessage($list, $LVM_SETCOLUMNWIDTH, [IntPtr]0, [IntPtr](-2)) | Out-Null   # nome do signatario inteiro
$lr = $AE::FromHandle($list).Current.BoundingRectangle
Click-At ([int]($lr.X + 60)) ([int]($lr.Y + 32))
Start-Sleep -Milliseconds 600
Shot $props 'instalador-assinaturas.png'
Click-ChildButton $props 'Detalhes'
$details = Wait-TopByTitle 'Detalhes de Assinatura Digital'
if (-not $details) { throw 'Detalhes de Assinatura Digital nao abriu.' }
Focus-Window ($AE::FromHandle($details))
Shot $details 'instalador-detalhes-assinatura.png'
Click-ChildButton $details 'Exibir Certificado'
$cert = Wait-TopByTitle 'Certificado'
if (-not $cert) { throw 'Janela Certificado nao abriu.' }
Focus-Window ($AE::FromHandle($cert))
Shot $cert 'instalador-certificado.png'
Select-SheetPage $cert 'Caminho*'
Shot $cert 'instalador-certificado-caminho.png'
foreach ($h in @($cert, $details, $props)) { [QWI]::SendMessage($h, $WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 500 }

# 2. Abrir o Setup baixado: SmartScreen (se aparecer) e o instalador.
Start-Process explorer.exe -ArgumentList "`"$copy`""
$smart = Wait-Until {
    $AE::RootElement.FindAll($TS::Children, (Cond $AE::ClassNameProperty 'ApplicationFrameWindow')) |
        Where-Object { $_.FindFirst($TS::Descendants, (Cond $AE::NameProperty 'Windows Defender SmartScreen')) } | Select-Object -First 1
} 15
if ($smart) {
    $sh = [IntPtr]$smart.Current.NativeWindowHandle
    Shot $sh 'instalador-smartscreen.png'
    $more = $smart.FindFirst($TS::Descendants, (Cond $AE::NameProperty 'Mais informações'))
    [QW]::SetForegroundWindow($sh) | Out-Null; Start-Sleep -Milliseconds 300
    $more.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Seconds 2
    Shot $sh 'instalador-smartscreen-fornecedor.png'
    (Find-Button $smart 'Executar assim mesmo').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
} else {
    'SmartScreen nao apareceu (o certificado ja tem reputacao ou o filtro esta desligado): capturas do SmartScreen puladas.'
}

# 3. Paginas do instalador (processo Setup_qdisk_<versao>.tmp extraido pelo Inno).
$wizard = Wait-Until {
    foreach ($p in Get-Process | Where-Object { $_.ProcessName -like 'Setup_qdisk*' }) {
        foreach ($h in [QWI]::TopOf([uint32]$p.Id)) { if ([QWI]::Cls($h) -eq 'TWizardForm') { return $h } }
    }
} 60
if (-not $wizard) { throw 'Assistente do instalador nao abriu.' }
function Get-WizardText { ([QWI]::Children($wizard) | Where-Object { [QWI]::IsWindowVisible($_) } | ForEach-Object { [QWI]::Text($_) }) -join ' | ' }
Wait-Until { if ((Get-WizardText) -like '*Tarefas Adicionais*') { $true } } 20 | Out-Null
Shot $wizard 'instalador-tarefas.png'
Click-ChildButton $wizard 'Avançar'
Wait-Until { if ((Get-WizardText) -like '*Pronto pra Instalar*') { $true } } 20 | Out-Null
Shot $wizard 'instalador-pronto.png'
Click-ChildButton $wizard 'Instalar'
if (Wait-Until { if ((Get-WizardText) -like '*Extraindo arquivos*') { $true } } 10 100) {
    Shot $wizard 'instalador-instalando.png'
    $pathLabel = [QWI]::Children($wizard) | Where-Object { [QWI]::IsWindowVisible($_) -and [QWI]::Text($_) -like '*\AppData\Local\qdisk\*' } | Select-Object -First 1
    if ($pathLabel) { Hide-ControlText $wizard $pathLabel 'instalador-instalando.png' 'C:\Users\<usuário>\AppData\Local\qdisk\qdisk.exe' }
}
Wait-Until { if ((Get-WizardText) -like '*Concluir*') { $true } } 180 | Out-Null
Shot $wizard 'instalador-concluido.png'
# Desmarca "Iniciar o Q-DISK" (primeiro item da lista) e conclui.
$check = Get-VisibleChild $wizard 'TNewCheckListBox'
$cr = $AE::FromHandle($check).Current.BoundingRectangle
[QW]::SetForegroundWindow($wizard) | Out-Null; Start-Sleep -Milliseconds 300
Click-At ([int]($cr.X + 10)) ([int]($cr.Y + 10))
Start-Sleep -Milliseconds 400
Click-ChildButton $wizard 'Concluir'

# 4. Conferencia do que foi instalado.
$installDir = Join-Path $env:LOCALAPPDATA 'qdisk'
foreach ($exe in Get-ChildItem $installDir -Filter *.exe) {
    $s = Get-AuthenticodeSignature $exe.FullName
    'instalado: {0,-14} {1,-6} {2}' -f $exe.Name, $s.Status, $s.SignerCertificate.Subject
}
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
"Capturas em $script:OutDir"
