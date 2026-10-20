# ─────────────────────────────────────────────────────────────────────────────
# restore_from_backup.ps1
# Recupera os arquivos do projeto a partir do ponto de backup em rede para a
# pasta local de sincronização com o GitHub.
#
# Uso (execute na máquina de sync, como Administrador se necessário):
#   .\restore_from_backup.ps1
#   .\restore_from_backup.ps1 -NoPush        # recupera sem chamar github_sync.py
#   .\restore_from_backup.ps1 -BackupHost 192.168.10.6   # host alternativo
#
# Senha do compartilhamento (fora do codigo), nesta ordem: -BackupPass, variavel
# QUALLIT_BACKUP_PASSWORD, linha 'BackupPassword:' em config\secret.md (fora do Git;
# modelo em config\secret.md.example) ou pergunta no terminal.
#
# O .git local NÃO é sobrescrito — o remote/branch configurado é preservado.
# ─────────────────────────────────────────────────────────────────────────────

param(
    [string] $BackupHost   = "192.168.10.5",
    [string] $BackupUser   = "mateweb",
    [string] $BackupPass   = "",
    [string] $BackupShare  = "store",

    # Subcaminho dentro do share onde o backup espelha C:\DESENV
    [string] $BackupSubPath = "QSI_BACKUP\DESENV\PROJECT_DESENV\qdisk",

    # Destino local (pasta de sync com o GitHub)
    [string] $LocalDest    = "C:\Desenv\GITHUB_SYNC\qdisk",

    # Após a recuperação, abre o github_sync.py automaticamente?
    [switch] $NoPush
)

# ── Cores ────────────────────────────────────────────────────────────────────
function Write-Step  { param($m) Write-Host "  [INFO ] $m" -ForegroundColor Cyan    }
function Write-Ok    { param($m) Write-Host "  [OK   ] $m" -ForegroundColor Green   }
function Write-Warn  { param($m) Write-Host "  [AVISO] $m" -ForegroundColor Yellow  }
function Write-Fail  { param($m) Write-Host "  [FALHA] $m" -ForegroundColor Red     }

# ── Senha (fora do codigo) ───────────────────────────────────────────────────
function Read-Secret {
    param([Parameter(Mandatory)] [string]$Name)
    $secretFile = Join-Path $PSScriptRoot '..\..\config\secret.md'
    if (-not (Test-Path -LiteralPath $secretFile)) { return '' }
    foreach ($line in Get-Content -LiteralPath $secretFile -Encoding UTF8) {
        $match = [regex]::Match($line, '^\s*[-*]?\s*([A-Za-z][A-Za-z0-9_]*)\s*:\s*(\S.*?)\s*$')
        if (-not $match.Success -or $match.Groups[1].Value -ne $Name) { continue }
        $value = $match.Groups[2].Value.Trim().Trim('`').Trim('"').Trim("'").Trim()
        if ($value.StartsWith('<') -and $value.EndsWith('>')) { return '' }
        return $value
    }
    return ''
}
if ([string]::IsNullOrWhiteSpace($BackupPass)) { $BackupPass = $env:QUALLIT_BACKUP_PASSWORD }
if ([string]::IsNullOrWhiteSpace($BackupPass)) { $BackupPass = Read-Secret -Name 'BackupPassword' }
if ([string]::IsNullOrWhiteSpace($BackupPass)) {
    $secure = Read-Host "Senha de $BackupUser em \\$BackupHost" -AsSecureString
    $BackupPass = [System.Net.NetworkCredential]::new('', $secure).Password
}

# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host ("=" * 56)
Write-Host "  qdisk — Restauração do backup" -ForegroundColor White
Write-Host ("=" * 56)
Write-Host "  Backup : \\$BackupHost\$BackupShare\$BackupSubPath"
Write-Host "  Destino: $LocalDest"
Write-Host ""

# ── 1. Mapear compartilhamento de rede ───────────────────────────────────────
$unc = "\\$BackupHost\$BackupShare"
Write-Step "Conectando ao backup: $unc ..."

# Limpa mapeamento anterior se existir (evita conflito de credenciais)
net use $unc /delete >$null 2>&1

$netOut = & net use $unc $BackupPass /user:$BackupUser 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Falha ao conectar: $netOut"
    Write-Warn "Verifique host ($BackupHost), usuário ($BackupUser) e senha."
    exit 1
}
Write-Ok "Conectado."

# ── 2. Validar origem ────────────────────────────────────────────────────────
$source = "$unc\$BackupSubPath"
if (-not (Test-Path $source)) {
    Write-Fail "Caminho de backup não encontrado: $source"
    net use $unc /delete >$null 2>&1
    exit 1
}

# ── 3. Criar destino se não existir ──────────────────────────────────────────
if (-not (Test-Path $LocalDest)) {
    Write-Step "Criando pasta de destino: $LocalDest"
    New-Item -ItemType Directory -Path $LocalDest -Force | Out-Null
}

# ── 4. Robocopy (exclui .git para preservar o remote configurado) ────────────
Write-Step "Copiando arquivos (robocopy)..."
Write-Host ""

$robocopyArgs = @(
    $source,
    $LocalDest,
    "/MIR",          # espelha (remove arquivos que sumiram da origem)
    "/W:1",          # wait 1s entre tentativas
    "/R:2",          # 2 tentativas por arquivo
    "/NFL",          # não lista cada arquivo (output mais limpo)
    "/NDL",          # não lista cada diretório
    "/NP",           # sem percentual
    # Exclusões de pastas (não sobrescreve .git local, pula lixo de build)
    "/XD", ".git", ".vs", "bin", "obj", "temp", "dist",
            "tools\log", "tools\build_log",
    # Exclusões de arquivos
    "/XF", "*.log", "*.user", "*.suo", "Thumbs.db", "Desktop.ini",
            "github_sync.ini"   # configuração de ambiente — não sincronizada
)

robocopy @robocopyArgs

# robocopy retorna 0=nada copiado, 1=ok-copiou, >=8=erro
if ($LASTEXITCODE -ge 8) {
    Write-Host ""
    Write-Fail "Robocopy encerrou com código $LASTEXITCODE — verifique o output acima."
    net use $unc /delete >$null 2>&1
    exit 1
}

Write-Host ""
Write-Ok "Arquivos recuperados com sucesso."

# ── 5. Desconectar share ─────────────────────────────────────────────────────
net use $unc /delete >$null 2>&1
Write-Step "Conexão de rede encerrada."

# ── 6. Verificar .git no destino ─────────────────────────────────────────────
$gitDir = Join-Path $LocalDest ".git"
if (-not (Test-Path $gitDir)) {
    Write-Warn ".git não encontrado em $LocalDest."
    Write-Warn "Configure o repositório antes de usar github_sync.py:"
    Write-Warn "  cd $LocalDest"
    Write-Warn "  git init && git remote add origin <URL>"
} else {
    Write-Ok ".git preservado (remote/branch intactos)."
}

# ── 7. Chamar github_sync.py (opcional) ──────────────────────────────────────
if (-not $NoPush) {
    $syncScript = Join-Path $LocalDest "tools\git\github_sync.py"
    if (Test-Path $syncScript) {
        Write-Host ""
        Write-Step "Iniciando sincronização com GitHub..."
        Write-Host ""
        Set-Location $LocalDest
        python $syncScript
    } else {
        Write-Warn "github_sync.py não encontrado. Execute manualmente."
    }
}

Write-Host ""
Write-Host ("=" * 56)
Write-Host ""
