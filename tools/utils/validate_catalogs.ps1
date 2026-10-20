param(
    [Parameter(Mandatory = $false)]
    [string]$CatalogDir = "config"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath $CatalogDir
$files = Get-ChildItem -LiteralPath $root -Filter "qdisk.quallit.*.json" -File | Sort-Object Name

if ($files.Count -eq 0) {
    throw "Nenhum catalogo qdisk.quallit.*.json encontrado em $root"
}

$totalItems = 0
foreach ($file in $files) {
    Write-Host "Validando $($file.Name)..."
    $rawJson = Get-Content -LiteralPath $file.FullName -Raw
    $items = $rawJson | ConvertFrom-Json
    if ($null -eq $items) {
        if ($file.Name -eq "qdisk.quallit.devteam.json" -and $rawJson.Trim() -eq "[]") {
            $items = @()
        }
        else {
        throw "$($file.Name): JSON vazio ou invalido."
        }
    }

    if ($items -isnot [System.Array]) {
        $items = @($items)
    }

    if ($items.Count -eq 0 -and $file.Name -ne "qdisk.quallit.devteam.json") {
        throw "$($file.Name): catalogo sem itens."
    }

    $index = 0
    foreach ($item in $items) {
        $index++
        if ($null -ne $item.enabled -and $item.enabled -isnot [bool]) {
            throw "$($file.Name): item $($item.name) com enabled invalido. Use true ou false."
        }
        if ([string]::IsNullOrWhiteSpace($item.name)) {
            throw "$($file.Name): item $index sem name."
        }
        if ([string]::IsNullOrWhiteSpace($item.group)) {
            throw "$($file.Name): item $($item.name) sem group."
        }
        if ([string]::IsNullOrWhiteSpace($item.fileUrl)) {
            throw "$($file.Name): item $($item.name) sem fileUrl."
        }
        if ($item.fileUrl -notmatch '^https?://') {
            throw "$($file.Name): item $($item.name) com fileUrl invalida."
        }
        if (-not [string]::IsNullOrWhiteSpace($item.md5) -and $item.md5 -notmatch '^[a-fA-F0-9]{32}$') {
            throw "$($file.Name): item $($item.name) com md5 invalido."
        }
        if (-not [string]::IsNullOrWhiteSpace($item.sha256) -and $item.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
            throw "$($file.Name): item $($item.name) com sha256 invalido."
        }
        if (-not [string]::IsNullOrWhiteSpace($item.sha256) -and ([int64]$item.imageSizeBytes) -le 0) {
            throw "$($file.Name): item $($item.name) com imageSizeBytes ausente."
        }
    }

    $totalItems += $items.Count
    Write-Host "OK: $($file.Name) ($($items.Count) itens)"
}

Write-Host "Catalogos validos: $($files.Count) arquivo(s), $totalItems item(ns)."
