# Script para extrair a cadeia de certificados de um executável assinado
# Uso: .\extract_cert_chain.ps1 -FilePath "C:\caminho\para\executavel.exe"

param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath,
    
    [Parameter(Mandatory=$false)]
    [string]$OutputDir = $PSScriptRoot
)

Write-Host "=== Extração de Cadeia de Certificados ===" -ForegroundColor Cyan
Write-Host "Arquivo: $FilePath" -ForegroundColor Yellow

if (-not (Test-Path $FilePath)) {
    Write-Host "ERRO: Arquivo não encontrado: $FilePath" -ForegroundColor Red
    exit 1
}

try {
    # Obter assinatura do arquivo
    $signature = Get-AuthenticodeSignature -FilePath $FilePath
    
    if ($null -eq $signature.SignerCertificate) {
        Write-Host "ERRO: Arquivo não está assinado ou não foi possível obter o certificado." -ForegroundColor Red
        exit 1
    }
    
    Write-Host "`nCertificado do Signatário:" -ForegroundColor Green
    Write-Host "  Subject: $($signature.SignerCertificate.Subject)" -ForegroundColor White
    Write-Host "  Issuer: $($signature.SignerCertificate.Issuer)" -ForegroundColor White
    Write-Host "  Thumbprint: $($signature.SignerCertificate.Thumbprint)" -ForegroundColor White
    
    # Construir cadeia de certificados
    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    $chain.Build($signature.SignerCertificate) | Out-Null
    
    Write-Host "`n=== Cadeia de Certificados ===" -ForegroundColor Cyan
    Write-Host "Total de certificados na cadeia: $($chain.ChainElements.Count)" -ForegroundColor Yellow
    
    # Criar diretório de saída se não existir
    if (-not (Test-Path $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
        Write-Host "Diretório criado: $OutputDir" -ForegroundColor Green
    }
    
    # Exportar cada certificado da cadeia
    $exported = 0
    for ($i = 0; $i -lt $chain.ChainElements.Count; $i++) {
        $chainCert = $chain.ChainElements[$i].Certificate
        $subject = $chainCert.Subject
        
        # Criar nome de arquivo seguro
        $safeName = $subject -replace '[<>:"/\\|?*]', '_'
        $safeName = $safeName -replace '\s+', '_'
        $safeName = $safeName.Substring(0, [Math]::Min(100, $safeName.Length))
        
        $filename = "cert_chain_$i`_$safeName.cer"
        $filepath = Join-Path $OutputDir $filename
        
        try {
            # Exportar como DER (binário)
            [System.IO.File]::WriteAllBytes($filepath, $chainCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
            
            Write-Host "`n[$i] Exportado: $filename" -ForegroundColor Green
            Write-Host "    Subject: $subject" -ForegroundColor White
            Write-Host "    Issuer: $($chainCert.Issuer)" -ForegroundColor Gray
            Write-Host "    Validade: $($chainCert.NotBefore) até $($chainCert.NotAfter)" -ForegroundColor Gray
            Write-Host "    Thumbprint: $($chainCert.Thumbprint)" -ForegroundColor Gray
            
            $exported++
        } catch {
            Write-Host "ERRO ao exportar certificado [$i]: $_" -ForegroundColor Red
        }
    }
    
    Write-Host "`n=== Resumo ===" -ForegroundColor Cyan
    Write-Host "Certificados exportados: $exported/$($chain.ChainElements.Count)" -ForegroundColor $(if ($exported -eq $chain.ChainElements.Count) { "Green" } else { "Yellow" })
    Write-Host "Diretório de saída: $OutputDir" -ForegroundColor Yellow
    
    # Verificar status da cadeia
    Write-Host "`n=== Status da Cadeia ===" -ForegroundColor Cyan
    if ($chain.ChainStatus.Count -eq 0) {
        Write-Host "Cadeia válida!" -ForegroundColor Green
    } else {
        Write-Host "Problemas na cadeia:" -ForegroundColor Yellow
        foreach ($status in $chain.ChainStatus) {
            Write-Host "  - $($status.Status): $($status.StatusInformation)" -ForegroundColor $(if ($status.Status -eq [System.Security.Cryptography.X509Certificates.X509ChainStatusFlags]::UntrustedRoot) { "Red" } else { "Yellow" })
        }
    }
    
} catch {
    Write-Host "ERRO: $_" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    exit 1
}

Write-Host "`nConcluído!" -ForegroundColor Green






