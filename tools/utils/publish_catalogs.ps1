<#
Publishes qdisk catalog JSON files without rebuilding the full
update package.

Default targets:
  - fallback static repository via scp/pscp
  - installer Git repository under public/qdisk

Local credentials are read from the same places used by publish_update_package.ps1:
environment variables, tools\.env, tools\publish_update.env, .env or
config\publish_update.env.
#>

[CmdletBinding()]
param(
    [string[]]$CatalogPath = @(),
    [string]$CatalogDir = '',
    [ValidateSet('Fallback', 'InstallerGit', 'Both')]
    [string]$Target = 'Both',
    [string]$HostName = 'mateweb.ddns.net',
    [int]$Port = 2222,
    [string]$HostKey = '',
    [string]$UserName = 'qdisk-update',
    [string]$RemotePath = '/store/repositorio/qdisk',
    [string]$SshPassword = '',
    [string]$EnvFile,
    [string]$RepositoryUrl = 'https://github.com/quallitdesenv/quallit-installer.git',
    [string]$RepositoryDir = '',
    [string]$Branch = '',
    [string]$TargetSubDir = 'public/qdisk',
    [string]$CommitMessage = '',
    [string]$RemoteBaseUrl = 'https://installer.quallit.com.br/qdisk',
    [int]$VerifyAttempts = 5,
    [int]$VerifyDelaySeconds = 10,
    [switch]$DryRun,
    [switch]$NoPush,
    [switch]$NoVerifyRemote
)

$ErrorActionPreference = 'Stop'

$scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
} else {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}
$toolsRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')
$repoRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot '..\..')
# Segredos locais (fora do Git): uma linha 'Chave: valor' por segredo. Modelo: config\secret.md.example
$secretFile = Join-Path $repoRoot 'config\secret.md'

if ([string]::IsNullOrWhiteSpace($CatalogDir)) {
    $CatalogDir = Join-Path $repoRoot 'config'
}

function Resolve-CommandPath {
    param([string[]]$Names)
    foreach ($name in $Names) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    return $null
}

function Import-DotEnvFile {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith('#')) {
            continue
        }

        $name, $value = $trimmed -split '=', 2
        if ([string]::IsNullOrWhiteSpace($name) -or $null -eq $value) {
            continue
        }

        $name = $name.Trim()
        $value = $value.Trim().Trim('"').Trim("'")
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, 'Process'))) {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
    return $true
}

function Read-PublishScriptDefaults {
    $path = Join-Path $scriptRoot 'publish_update_package.ps1'
    if (-not (Test-Path -LiteralPath $path)) {
        return @{}
    }

    $text = Get-Content -LiteralPath $path -Raw
    $defaults = @{}
    foreach ($name in @('HostName', 'Port', 'UserName', 'RemotePath')) {
        $pattern = ('\${0}\s*=\s*''([^'']*)''' -f [regex]::Escape($name))
        $match = [regex]::Match($text, $pattern)
        if ($match.Success) {
            $defaults[$name] = $match.Groups[1].Value
        }
    }
    return $defaults
}

function Read-Secret {
    param([Parameter(Mandatory)] [string]$Name)
    if (-not (Test-Path -LiteralPath $secretFile)) {
        return ''
    }

    foreach ($line in Get-Content -LiteralPath $secretFile -Encoding UTF8) {
        $match = [regex]::Match($line, '^\s*[-*]?\s*([A-Za-z][A-Za-z0-9_]*)\s*:\s*(\S.*?)\s*$')
        if (-not $match.Success -or $match.Groups[1].Value -ne $Name) {
            continue
        }
        $value = $match.Groups[2].Value.Trim().Trim('`').Trim('"').Trim("'").Trim()
        if ($value.StartsWith('<') -and $value.EndsWith('>')) {
            return ''
        }
        return $value
    }
    return ''
}

function Read-EndpointConfigDefaults {
    $path = Join-Path $repoRoot 'config\qdisk.endpoints.json'
    if (-not (Test-Path -LiteralPath $path)) {
        return @{}
    }

    try {
        $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
    catch {
        return @{}
    }

    $defaults = @{}
    if ($json.fallback) {
        if (-not [string]::IsNullOrWhiteSpace($json.fallback.publicBaseUrl)) { $defaults['FallbackPublicBaseUrl'] = ([string]$json.fallback.publicBaseUrl).TrimEnd('/') }
        if (-not [string]::IsNullOrWhiteSpace($json.fallback.host)) { $defaults['HostName'] = [string]$json.fallback.host }
        if ($null -ne $json.fallback.port -and -not [string]::IsNullOrWhiteSpace([string]$json.fallback.port)) { $defaults['Port'] = [string]$json.fallback.port }
        if (-not [string]::IsNullOrWhiteSpace($json.fallback.hostKey)) { $defaults['HostKey'] = [string]$json.fallback.hostKey }
        if (-not [string]::IsNullOrWhiteSpace($json.fallback.user)) { $defaults['UserName'] = [string]$json.fallback.user }
        if (-not [string]::IsNullOrWhiteSpace($json.fallback.remotePath)) { $defaults['RemotePath'] = [string]$json.fallback.remotePath }
    }
    if ($json.installerGit) {
        if (-not [string]::IsNullOrWhiteSpace($json.installerGit.repositoryUrl)) { $defaults['RepositoryUrl'] = [string]$json.installerGit.repositoryUrl }
        if (-not [string]::IsNullOrWhiteSpace($json.installerGit.targetSubdir)) { $defaults['TargetSubDir'] = [string]$json.installerGit.targetSubdir }
    }
    if ($json.primary -and -not [string]::IsNullOrWhiteSpace($json.primary.baseUrl)) {
        $defaults['RemoteBaseUrl'] = ([string]$json.primary.baseUrl).TrimEnd('/')
    }
    return $defaults
}

function Invoke-GitChecked {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [string]$WorkingDirectory = ''
    )

    $previousLocation = Get-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Set-Location -LiteralPath $WorkingDirectory
        }

        & git @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "git $($Arguments -join ' ') falhou com codigo $LASTEXITCODE."
        }
    }
    finally {
        Set-Location -LiteralPath $previousLocation
    }
}

function Get-StringSha256 {
    param([Parameter(Mandatory)] [string]$Value)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return -join ($sha.ComputeHash($bytes) | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $sha.Dispose()
    }
}

function Get-JsonSemanticSha256 {
    param([Parameter(Mandatory)] [string]$Path)

    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $canonical = $json | ConvertTo-Json -Depth 100 -Compress
    return Get-StringSha256 -Value $canonical
}

function Test-RemoteJsonFile {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo]$LocalFile,
        [Parameter(Mandatory)] [string]$BaseUrl,
        [bool]$CacheBust = $false
    )

    $url = $BaseUrl.TrimEnd('/') + '/' + [uri]::EscapeDataString($LocalFile.Name)
    $requestUrl = $url
    if ($CacheBust) {
        $separator = if ($requestUrl.Contains('?')) { '&' } else { '?' }
        $requestUrl = $requestUrl + $separator + 'verify=' + [uri]::EscapeDataString([guid]::NewGuid().ToString('N'))
    }
    $tempFile = Join-Path $env:TEMP ('quallit-catalog-verify-' + [guid]::NewGuid().ToString('N'))
    try {
        Invoke-WebRequest -Uri $requestUrl -OutFile $tempFile -UseBasicParsing -TimeoutSec 120
        $localHash = Get-JsonSemanticSha256 -Path $LocalFile.FullName
        $remoteHash = Get-JsonSemanticSha256 -Path $tempFile
        if ($remoteHash -ne $localHash) {
            throw "SHA-256 semantico remoto divergente para $url. Esperado=$localHash Remoto=$remoteHash"
        }
    }
    finally {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
    }

    Write-Host "OK remoto: $url" -ForegroundColor Green
}

function Test-RemoteJsonFiles {
    param(
        [Parameter(Mandatory)] [System.IO.FileInfo[]]$Files,
        [Parameter(Mandatory)] [string]$BaseUrl,
        [int]$Attempts,
        [int]$DelaySeconds,
        [bool]$CacheBust
    )

    if ($Attempts -lt 1) { $Attempts = 1 }
    if ($DelaySeconds -lt 0) { $DelaySeconds = 0 }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Write-Host "Verificando catalogos publicados em $BaseUrl (tentativa $attempt/$Attempts)..." -ForegroundColor Cyan
            foreach ($file in $Files) {
                Test-RemoteJsonFile -LocalFile $file -BaseUrl $BaseUrl -CacheBust $CacheBust
            }
            return
        }
        catch {
            if ($attempt -ge $Attempts) {
                throw
            }
            Write-Warning "Verificacao remota ainda nao confirmou a publicacao: $($_.Exception.Message)"
            if ($DelaySeconds -gt 0) {
                Write-Host "Aguardando $DelaySeconds segundo(s) antes de nova tentativa..." -ForegroundColor Yellow
                Start-Sleep -Seconds $DelaySeconds
            }
        }
    }
}

function Validate-CatalogName {
    param([Parameter(Mandatory)] [System.IO.FileInfo]$File)

    if ($File.Name -notmatch '^qdisk\.quallit\.(pnb|rd|tools|devteam)\.json$') {
        throw "Catalogo nao oficial para upload automatico: $($File.Name)"
    }
    $null = Get-Content -LiteralPath $File.FullName -Raw | ConvertFrom-Json
}

function Validate-ManifestFile {
    param([Parameter(Mandatory)] [System.IO.FileInfo]$File)

    if ($File.Name -notmatch '^[A-Za-z0-9._-]+\.manifest\.json$') {
        throw "Manifesto com nome invalido para upload automatico: $($File.Name)"
    }
    $null = Get-Content -LiteralPath $File.FullName -Raw | ConvertFrom-Json
}

$candidateEnvFiles = @()
if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
    $candidateEnvFiles += $EnvFile
}
$candidateEnvFiles += @(
    (Join-Path $toolsRoot '.env'),
    (Join-Path $toolsRoot 'publish_update.env'),
    (Join-Path $repoRoot '.env'),
    (Join-Path $repoRoot 'config\publish_update.env')
)

foreach ($candidate in $candidateEnvFiles) {
    if (Import-DotEnvFile -Path $candidate) {
        Write-Host "Variaveis carregadas de: $candidate" -ForegroundColor Cyan
        break
    }
}

$endpointDefaults = Read-EndpointConfigDefaults
$publishDefaults = Read-PublishScriptDefaults

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_UPDATE_HOST)) { $HostName = $env:QUALLIT_UPDATE_HOST }
elseif (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_PUBLISH_HOST)) { $HostName = $env:QUALLIT_PUBLISH_HOST }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['HostName'])) { $HostName = $endpointDefaults['HostName'] }
elseif (-not [string]::IsNullOrWhiteSpace($publishDefaults['HostName'])) { $HostName = $publishDefaults['HostName'] }

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_UPDATE_PORT)) { $Port = [int]$env:QUALLIT_UPDATE_PORT }
elseif (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_PUBLISH_PORT)) { $Port = [int]$env:QUALLIT_PUBLISH_PORT }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['Port'])) { $Port = [int]$endpointDefaults['Port'] }
elseif (-not [string]::IsNullOrWhiteSpace($publishDefaults['Port'])) { $Port = [int]$publishDefaults['Port'] }

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_UPDATE_HOST_KEY)) { $HostKey = $env:QUALLIT_UPDATE_HOST_KEY }
elseif (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_PUBLISH_HOST_KEY)) { $HostKey = $env:QUALLIT_PUBLISH_HOST_KEY }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['HostKey'])) { $HostKey = $endpointDefaults['HostKey'] }

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_UPDATE_USER)) { $UserName = $env:QUALLIT_UPDATE_USER }
elseif (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_PUBLISH_USER)) { $UserName = $env:QUALLIT_PUBLISH_USER }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['UserName'])) { $UserName = $endpointDefaults['UserName'] }
elseif (-not [string]::IsNullOrWhiteSpace($publishDefaults['UserName'])) { $UserName = $publishDefaults['UserName'] }

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_UPDATE_REMOTE_PATH)) { $RemotePath = $env:QUALLIT_UPDATE_REMOTE_PATH }
elseif (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_PUBLISH_REMOTE_PATH)) { $RemotePath = $env:QUALLIT_PUBLISH_REMOTE_PATH }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['RemotePath'])) { $RemotePath = $endpointDefaults['RemotePath'] }
elseif (-not [string]::IsNullOrWhiteSpace($publishDefaults['RemotePath'])) { $RemotePath = $publishDefaults['RemotePath'] }

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_UPDATE_SSH_PASSWORD)) { $SshPassword = $env:QUALLIT_UPDATE_SSH_PASSWORD }
elseif (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_PUBLISH_SSH_PASSWORD)) { $SshPassword = $env:QUALLIT_PUBLISH_SSH_PASSWORD }
elseif (-not [string]::IsNullOrWhiteSpace((Read-Secret -Name 'SshPassword'))) { $SshPassword = Read-Secret -Name 'SshPassword' }

if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_INSTALLER_REPO_URL)) { $RepositoryUrl = $env:QUALLIT_INSTALLER_REPO_URL }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['RepositoryUrl'])) { $RepositoryUrl = $endpointDefaults['RepositoryUrl'] }
if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_INSTALLER_REPO_DIR)) { $RepositoryDir = $env:QUALLIT_INSTALLER_REPO_DIR }
if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_INSTALLER_BRANCH)) { $Branch = $env:QUALLIT_INSTALLER_BRANCH }
if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_INSTALLER_TARGET_SUBDIR)) { $TargetSubDir = $env:QUALLIT_INSTALLER_TARGET_SUBDIR }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['TargetSubDir'])) { $TargetSubDir = $endpointDefaults['TargetSubDir'] }
if (-not [string]::IsNullOrWhiteSpace($env:QUALLIT_INSTALLER_REMOTE_BASE_URL)) { $RemoteBaseUrl = $env:QUALLIT_INSTALLER_REMOTE_BASE_URL }
elseif (-not [string]::IsNullOrWhiteSpace($endpointDefaults['RemoteBaseUrl'])) { $RemoteBaseUrl = $endpointDefaults['RemoteBaseUrl'] }

$catalogFiles = @()
if ($CatalogPath.Count -gt 0) {
    foreach ($path in $CatalogPath) {
        $catalogFiles += Get-Item -LiteralPath $path
    }
}
else {
    $catalogFiles = Get-ChildItem -LiteralPath $CatalogDir -Filter 'qdisk.*.json' -File | Sort-Object Name
}

if ($catalogFiles.Count -eq 0) {
    throw 'Nenhum catalogo encontrado para upload.'
}

foreach ($file in $catalogFiles) {
    Validate-CatalogName -File $file
}

$manifestDir = Join-Path $CatalogDir 'manifests'
$manifestFiles = @()
if (Test-Path -LiteralPath $manifestDir) {
    $manifestFiles = Get-ChildItem -LiteralPath $manifestDir -Filter '*.manifest.json' -File | Sort-Object Name
    foreach ($file in $manifestFiles) {
        Validate-ManifestFile -File $file
    }
}

Write-Host 'Catalogos preparados para upload:' -ForegroundColor Cyan
$catalogFiles | Select-Object Name, Length | Format-Table -AutoSize
if ($manifestFiles.Count -gt 0) {
    Write-Host 'Manifestos preparados para upload:' -ForegroundColor Cyan
    $manifestFiles | Select-Object Name, Length | Format-Table -AutoSize
}

if ($Target -eq 'Fallback' -or $Target -eq 'Both') {
    $payloadDir = Join-Path $env:TEMP ('quallit-catalog-publish-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $payloadDir | Out-Null
    try {
        foreach ($file in $catalogFiles) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $payloadDir $file.Name) -Force
        }
        if ($manifestFiles.Count -gt 0) {
            $payloadManifestDir = Join-Path $payloadDir 'manifests'
            New-Item -ItemType Directory -Path $payloadManifestDir -Force | Out-Null
            foreach ($file in $manifestFiles) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $payloadManifestDir $file.Name) -Force
            }
        }

        $targetPath = "$UserName@$HostName`:$RemotePath/"
        Write-Host "Destino fallback: $targetPath" -ForegroundColor Cyan

        if (-not $DryRun) {
            $pscp = Resolve-CommandPath @('pscp.exe', 'pscp')
            $scp = Resolve-CommandPath @('scp.exe', 'scp')

            if ($pscp) {
                $arguments = @('-batch', '-r', '-P', [string]$Port)
                if (-not [string]::IsNullOrWhiteSpace($HostKey)) {
                    $arguments += @('-hostkey', $HostKey)
                }
                # -pwfile (PuTTY >= 0.77) evita expor a senha na lista de processos.
                $pwFile = $null
                if (-not [string]::IsNullOrWhiteSpace($SshPassword)) {
                    $pwFile = [System.IO.Path]::GetTempFileName()
                    [System.IO.File]::WriteAllText($pwFile, $SshPassword)
                    $arguments += @('-pwfile', $pwFile)
                }
                $arguments += @((Join-Path $payloadDir '*'), $targetPath)
                try {
                    & $pscp @arguments
                }
                finally {
                    if ($pwFile) { Remove-Item -LiteralPath $pwFile -Force -ErrorAction SilentlyContinue }
                }
                if ($LASTEXITCODE -ne 0) {
                    if ([string]::IsNullOrWhiteSpace($SshPassword)) {
                        throw "pscp falhou com codigo $LASTEXITCODE. Nenhuma senha foi resolvida de QUALLIT_UPDATE_SSH_PASSWORD, QUALLIT_PUBLISH_SSH_PASSWORD ou SshPassword em config\secret.md; se usar chave SSH, confirme que o host esta confiavel/cacheado e a chave esta carregada."
                    }
                    throw "pscp falhou com codigo $LASTEXITCODE. Se a mensagem citar prompt interativo, aceite a chave do host uma vez fora do GUI ou configure a chave/senha usada pelo build."
                }
            }
            elseif ($scp) {
                $sourcePattern = Join-Path $payloadDir '*'
                & $scp -r -P $Port -o BatchMode=yes $sourcePattern $targetPath
                if ($LASTEXITCODE -ne 0) { throw "scp falhou com codigo $LASTEXITCODE." }
            }
            else {
                throw 'Nenhum cliente scp encontrado. Instale PuTTY/pscp ou OpenSSH Client.'
            }
        }
        else {
            Write-Host 'DryRun ativo: nenhum arquivo foi enviado ao fallback.' -ForegroundColor Yellow
        }

        if (-not $DryRun -and -not $NoVerifyRemote) {
            $fallbackBaseUrl = if (-not [string]::IsNullOrWhiteSpace($endpointDefaults['FallbackPublicBaseUrl'])) { $endpointDefaults['FallbackPublicBaseUrl'] } else { "https://$HostName/repositorio/qdisk" }
            Test-RemoteJsonFiles -Files $catalogFiles -BaseUrl $fallbackBaseUrl -Attempts 1 -DelaySeconds 0 -CacheBust $false
            if ($manifestFiles.Count -gt 0) {
                Test-RemoteJsonFiles -Files $manifestFiles -BaseUrl ($fallbackBaseUrl.TrimEnd('/') + '/manifests') -Attempts 1 -DelaySeconds 0 -CacheBust $false
            }
        }
    }
    finally {
        Remove-Item -LiteralPath $payloadDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($Target -eq 'InstallerGit' -or $Target -eq 'Both') {
    $createdTempRepo = $false
    $repoDir = $RepositoryDir
    if ([string]::IsNullOrWhiteSpace($repoDir)) {
        $repoDir = Join-Path $env:TEMP ('quallit-installer-catalogs-' + [guid]::NewGuid().ToString('N'))
        $cloneArgs = @('clone', '--depth', '1')
        if (-not [string]::IsNullOrWhiteSpace($Branch)) {
            $cloneArgs += @('--branch', $Branch)
        }
        $cloneArgs += @($RepositoryUrl, $repoDir)
        Invoke-GitChecked -Arguments $cloneArgs
        $createdTempRepo = $true
    }
    else {
        $repoDir = (Resolve-Path -LiteralPath $repoDir).Path
        if (-not (Test-Path -LiteralPath (Join-Path $repoDir '.git'))) {
            throw "Diretorio nao e um repositorio Git: $repoDir"
        }
        if (-not [string]::IsNullOrWhiteSpace($Branch)) {
            Invoke-GitChecked -Arguments @('checkout', $Branch) -WorkingDirectory $repoDir
        }
        Invoke-GitChecked -Arguments @('pull', '--ff-only') -WorkingDirectory $repoDir
    }

    try {
        $targetDir = Join-Path $repoDir $TargetSubDir
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

        foreach ($file in $catalogFiles) {
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetDir $file.Name) -Force
        }
        if ($manifestFiles.Count -gt 0) {
            $targetManifestDir = Join-Path $targetDir 'manifests'
            New-Item -ItemType Directory -Path $targetManifestDir -Force | Out-Null
            foreach ($file in $manifestFiles) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $targetManifestDir $file.Name) -Force
            }
        }

        Write-Host "Catalogos preparados em $TargetSubDir" -ForegroundColor Cyan
        $catalogFiles | Select-Object Name, Length | Format-Table -AutoSize
        if ($manifestFiles.Count -gt 0) {
            Write-Host "Manifestos preparados em $TargetSubDir/manifests" -ForegroundColor Cyan
            $manifestFiles | Select-Object Name, Length | Format-Table -AutoSize
        }

        $relativePaths = @()
        foreach ($file in $catalogFiles) {
            $relativePaths += ($TargetSubDir.TrimEnd('/').TrimEnd('\\') + '/' + $file.Name)
        }
        foreach ($file in $manifestFiles) {
            $relativePaths += ($TargetSubDir.TrimEnd('/').TrimEnd('\\') + '/manifests/' + $file.Name)
        }

        $statusArgs = @('-C', $repoDir, 'status', '--short', '--') + $relativePaths
        $status = (& git @statusArgs) | Out-String
        if ([string]::IsNullOrWhiteSpace($status.Trim())) {
            Write-Host 'Nenhuma alteracao de catalogo para publicar no repositorio quallit-installer.' -ForegroundColor Yellow
        }
        elseif ($DryRun) {
            Write-Host 'Alteracoes detectadas:' -ForegroundColor Cyan
            Write-Host $status
            Write-Host 'DryRun ativo: nenhum commit/push foi executado.' -ForegroundColor Yellow
        }
        else {
            Write-Host 'Alteracoes detectadas:' -ForegroundColor Cyan
            Write-Host $status
            Invoke-GitChecked -Arguments (@('add', '--') + $relativePaths) -WorkingDirectory $repoDir
            if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
                $CommitMessage = 'chore: publica catalogos qdisk'
            }
            Invoke-GitChecked -Arguments @('commit', '-m', $CommitMessage) -WorkingDirectory $repoDir
            if ($NoPush) {
                Write-Host 'NoPush ativo: commit criado, push nao executado.' -ForegroundColor Yellow
            }
            else {
                Invoke-GitChecked -Arguments @('push') -WorkingDirectory $repoDir
                Write-Host 'Publicacao de catalogos no quallit-installer concluida.' -ForegroundColor Green
            }
        }

        if (-not $DryRun -and -not $NoPush -and -not $NoVerifyRemote) {
            Test-RemoteJsonFiles -Files $catalogFiles -BaseUrl $RemoteBaseUrl -Attempts $VerifyAttempts -DelaySeconds $VerifyDelaySeconds -CacheBust $true
            if ($manifestFiles.Count -gt 0) {
                Test-RemoteJsonFiles -Files $manifestFiles -BaseUrl ($RemoteBaseUrl.TrimEnd('/') + '/manifests') -Attempts $VerifyAttempts -DelaySeconds $VerifyDelaySeconds -CacheBust $true
            }
        }
    }
    finally {
        if ($createdTempRepo -and (Test-Path -LiteralPath $repoDir)) {
            Remove-Item -LiteralPath $repoDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Upload de catalogo concluido.' -ForegroundColor Green