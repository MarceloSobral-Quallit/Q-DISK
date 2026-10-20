param(
    # Padrao: pasta real de cache de imagens do qdisk (mesmo default de
    # AppSettings.GetDefaultImageCacheDir em Core/Models/AppSettings.vb), nao
    # uma pasta de scratch do projeto.
    [Parameter(Mandatory = $false)]
    [string]$InputDir = (Join-Path $env:USERPROFILE "Downloads\qdisk"),

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "",

    [Parameter(Mandatory = $false)]
    [string[]]$Extensions = @(".iso", ".tibx", ".img")
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath $InputDir
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $root "hashes"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$allowed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($ext in $Extensions) {
    if (-not $ext.StartsWith(".")) {
        $ext = ".$ext"
    }
    [void]$allowed.Add($ext)
}

$summary = @()
$files = Get-ChildItem -LiteralPath $root -File | Where-Object { $allowed.Contains($_.Extension) } | Sort-Object Name

foreach ($file in $files) {
    Write-Host "Hashing $($file.Name)..."
    $sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    $md5 = (Get-FileHash -Algorithm MD5 -LiteralPath $file.FullName).Hash.ToLowerInvariant()

    Set-Content -LiteralPath (Join-Path $OutputDir "$($file.Name).sha256") -Value "$sha256  $($file.Name)" -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $OutputDir "$($file.Name).size") -Value ([string]$file.Length) -Encoding ASCII

    $manifest = [ordered]@{
        fileName = $file.Name
        sizeBytes = $file.Length
        sha256 = $sha256
        md5 = $md5
        generatedAt = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDir "$($file.Name).manifest.json") -Encoding UTF8

    $summary += [pscustomobject]@{
        file = $file.Name
        sizeBytes = $file.Length
        sha256 = $sha256
        md5 = $md5
    }
}

$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDir "hash_summary.json") -Encoding UTF8
$summary | Format-Table -AutoSize
