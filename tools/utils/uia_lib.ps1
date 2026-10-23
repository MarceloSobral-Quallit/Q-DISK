# qdisk | Tools / Utils | uia_lib.ps1 - apoio de UI Automation para capturar_manual.ps1
Add-Type -AssemblyName System.Drawing, System.Windows.Forms, UIAutomationClient, UIAutomationTypes
if (-not ('QW' -as [type])) {
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class QW {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    // Clica (BM_CLICK) no botao nativo cujo texto, sem '&', e igual a 'text'. Devolve false se nao achar.
    public static bool ClickDialogButton(IntPtr dialog, string text) {
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowEx(dialog, child, "Button", null)) != IntPtr.Zero) {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(child, sb, sb.Capacity);
            if (string.Equals(sb.ToString().Replace("&", ""), text, StringComparison.OrdinalIgnoreCase)) {
                SendMessage(child, 0x00F5, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
        }
        return false;
    }
    public static string DialogText(IntPtr dialog) {
        var list = new System.Collections.Generic.List<string>();
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowEx(dialog, child, "Static", null)) != IntPtr.Zero) {
            var sb = new System.Text.StringBuilder(4096); GetWindowText(child, sb, sb.Capacity);
            if (sb.Length > 0) list.Add(sb.ToString());
        }
        return string.Join(" ", list);
    }
    public static string DialogButtons(IntPtr dialog) {
        var list = new System.Collections.Generic.List<string>();
        IntPtr child = IntPtr.Zero;
        while ((child = FindWindowEx(dialog, child, "Button", null)) != IntPtr.Zero) {
            var sb = new System.Text.StringBuilder(256); GetWindowText(child, sb, sb.Capacity); list.Add(sb.ToString());
        }
        return string.Join("|", list);
    }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@
}
$AE = [Windows.Automation.AutomationElement]
$TS = [Windows.Automation.TreeScope]
$CT = [Windows.Automation.ControlType]

$script:Results = New-Object System.Collections.Generic.List[object]
$script:Steps = 0

function Cond($prop, $val) { New-Object Windows.Automation.PropertyCondition($prop, $val) }

function Get-AppWindows([int] $procId) {
    $AE::RootElement.FindAll($TS::Children, (Cond $AE::ProcessIdProperty $procId))
}

# Janela que fecha durante a busca (ex.: a de autenticacao depois do login) lanca ElementNotAvailableException:
# conta como "ainda nao achou" e a busca continua, em vez de derrubar o roteiro.
function Wait-Until([scriptblock] $test, [int] $timeoutSec = 30, [int] $pollMs = 300) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        $r = $null
        try { $r = & $test } catch [System.Windows.Automation.ElementNotAvailableException] { $r = $null }
        if ($r) { return $r }
        Start-Sleep -Milliseconds $pollMs
    }
    return $null
}

function Wait-Window([int] $procId, [string] $namePattern, [int] $timeoutSec = 40) {
    Wait-Until { foreach ($w in Get-AppWindows $procId) { if ($w.Current.Name -like $namePattern) { return $w } } } $timeoutSec
}

# Caixa de mensagem (#32770) do processo; devolve @{ Window; Title; Text }
function Wait-MessageBox([int] $procId, [int] $timeoutSec = 15) {
    $w = Wait-Until {
        foreach ($w in Get-AppWindows $procId) { if ($w.Current.ClassName -eq '#32770') { return $w } }
        # caixa filha da janela principal
        foreach ($top in Get-AppWindows $procId) {
            $d = $top.FindFirst($TS::Children, (Cond $AE::ClassNameProperty '#32770'))
            if ($d) { return $d }
        }
    } $timeoutSec
    if (-not $w) { return $null }
    try {
        $texts = foreach ($t in $w.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) { $t.Current.Name }
        $text = ($texts -join ' ').Trim()
        if (-not $text) { $text = [QW]::DialogText([IntPtr]$w.Current.NativeWindowHandle).Trim() }
        [pscustomobject]@{ Window = $w; Title = $w.Current.Name; Text = ($text -replace '\s+', ' ') }
    } catch [System.Windows.Automation.ElementNotAvailableException] {
        return $null   # a caixa fechou enquanto era lida
    }
}

function Find-Button($root, [string] $name) {
    foreach ($el in $root.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Button))) {
        if ($el.Current.Name.Trim() -eq $name) { return $el }
    }
    return $null
}

# Clique assincrono (PostMessage BM_CLICK): o InvokePattern so retorna quando uma janela modal
# aberta pelo clique fecha, o que travaria o roteiro.
function Invoke-Button($root, [string] $name) {
    $b = Find-Button $root $name
    if (-not $b) { throw "Botao '$name' nao encontrado" }
    $h = [IntPtr]$b.Current.NativeWindowHandle
    if ($h -ne [IntPtr]::Zero) { [QW]::PostMessage($h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null }
    else { $b.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
    Start-Sleep -Milliseconds 300
}

function Is-Enabled($root, [string] $name) {
    $b = Find-Button $root $name
    if ($b) { return $b.Current.IsEnabled }
    return $null
}

function Close-MessageBox($mb, [string] $button = 'OK') {
    $hwnd = [IntPtr]$mb.Window.Current.NativeWindowHandle
    if (-not [QW]::ClickDialogButton($hwnd, $button)) {
        throw "Botao '$button' nao encontrado na mensagem '$($mb.Title)' (botoes: $([QW]::DialogButtons($hwnd)))"
    }
    Start-Sleep -Milliseconds 400
}

# Fecha qualquer mensagem: "Sim" quando existir, senao "OK". Devolve o botao usado.
function Close-AnyMessage($mb) {
    $buttons = [QW]::DialogButtons([IntPtr]$mb.Window.Current.NativeWindowHandle) -replace '&', ''
    # "Deseja ejetar o USB?" -> Nao: ejetar tira o USB do sistema e quebra as capturas seguintes.
    $choice = if ($mb.Text -match 'ejetar' -and $buttons -match '(^|\|)(N[aã]o)(\||$)') { $Matches[2] } elseif ($buttons -match '(^|\|)Sim(\||$)') { 'Sim' } else { 'OK' }
    Close-MessageBox $mb $choice
    return $choice
}

function Set-Text($root, [string] $value) {
    $edit = $root.FindFirst($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Edit))
    if (-not $edit) { throw 'Campo de texto nao encontrado' }
    $edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Click-At([int] $x, [int] $y) {
    [QW]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [QW]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    [QW]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
}

function Click-Element($el) {
    $r = $el.Current.BoundingRectangle
    Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2))
}

function Focus-Window($win, [int] $w = 0, [int] $h = 0) {
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    [QW]::ShowWindow($hwnd, 9) | Out-Null
    if ($w -gt 0) { [QW]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 40, $w, $h, 0x0044) | Out-Null }
    else { [QW]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 40, 0, 0, 0x0045) | Out-Null }
    [QW]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 500
}

# Captura so a janela: PrintWindow (PW_RENDERFULLCONTENT) desenha o conteudo dela mesmo com outra janela por cima
# (ex.: janela "sempre visivel" de uma chamada de video); o recorte segue a moldura do DWM, sem as bordas invisiveis.
# Se o PrintWindow falhar, copia a regiao da tela.
function Save-Shot($win, [string] $file) {
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    [QW]::SetForegroundWindow($hwnd) | Out-Null
    Start-Sleep -Milliseconds 600
    $r = New-Object QW+RECT
    [QW]::DwmGetWindowAttribute($hwnd, 9, [ref]$r, 16) | Out-Null
    $wr = New-Object QW+RECT
    [QW]::GetWindowRect($hwnd, [ref]$wr) | Out-Null
    $full = New-Object Drawing.Bitmap ($wr.R - $wr.L), ($wr.B - $wr.T)
    $g = [Drawing.Graphics]::FromImage($full)
    $hdc = $g.GetHdc()
    $printed = [QW]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
    $g = [Drawing.Graphics]::FromImage($bmp)
    if ($printed) {
        $g.DrawImage($full, (New-Object Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height), ($r.L - $wr.L), ($r.T - $wr.T), $bmp.Width, $bmp.Height, [Drawing.GraphicsUnit]::Pixel)
    } else {
        $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    }
    $path = Join-Path $script:OutDir $file
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose(); $full.Dispose()
    return $file
}

# Log do app: guarda a posicao e devolve so as linhas novas
function Mark-Log { $script:LogMark = if (Test-Path $script:LogPath) { (Get-Item $script:LogPath).Length } else { 0 } }
function Get-NewLog {
    if (-not (Test-Path $script:LogPath)) { return @() }
    $fs = [IO.File]::Open($script:LogPath, 'Open', 'Read', 'ReadWrite')
    try {
        $fs.Seek($script:LogMark, 'Begin') | Out-Null
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $text = $sr.ReadToEnd()
    } finally { $fs.Dispose() }
    ($text -split "`r?`n") | Where-Object { $_ } | ForEach-Object { ($_ -split ' \| ', 4)[3] }
}
function Wait-Log([string] $pattern, [int] $timeoutSec = 60) {
    Wait-Until { $hit = Get-NewLog | Where-Object { $_ -match $pattern } | Select-Object -Last 1; if ($hit) { $hit } } $timeoutSec 1000
}

# Linha de status (texto da StatusLine) de uma tela: o Label com o texto atual
function Get-StatusText($win, [string[]] $prefixes) {
    foreach ($t in $win.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text))) {
        $n = $t.Current.Name
        foreach ($p in $prefixes) { if ($n -like "$p*") { return $n } }
    }
    return $null
}
