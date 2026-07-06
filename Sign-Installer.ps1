$ErrorActionPreference = "Stop"

$certName = "BYLD Core Local Dev Cert"
$publishDir = "D:\Github Repos\PC-Stats-Monitor\bin\Release\net10.0-windows\publish"
$exePath = "D:\Github Repos\PC-Stats-Monitor\InstallerOutput\BYLD_PC_Stats_Monitor_Setup.exe"

# 1. Find or create the self-signed certificate
$cert = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Subject -match $certName } | Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating new self-signed certificate for local development..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Subject "CN=$certName" -Type CodeSigningCert -CertStoreLocation "Cert:\LocalMachine\My"
    
    Write-Host "Copying certificate to Trusted Root Certification Authorities so Windows trusts it..." -ForegroundColor Cyan
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store "Root", "LocalMachine"
    $store.Open("ReadWrite")
    $store.Add($cert)
    $store.Close()
} else {
    Write-Host "Found existing local certificate: $certName" -ForegroundColor Green
}

# 2. Sign all UN-SIGNED DLLs and EXEs in the publish directory FIRST
if (Test-Path $publishDir) {
    Write-Host "Scanning application files for missing signatures..." -ForegroundColor Cyan
    $filesToSign = Get-ChildItem -Path $publishDir -Include *.dll,*.exe -Recurse
    
    $signedCount = 0
    foreach ($file in $filesToSign) {
        $sig = Get-AuthenticodeSignature -FilePath $file.FullName
        # Only sign if it is NOT already validly signed (to avoid breaking official Microsoft signatures)
        if ($sig.Status -ne "Valid") {
            Write-Host "Signing unsigned file: $($file.Name)" -ForegroundColor DarkGray
            $signResult = Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert -HashAlgorithm SHA256
            if ($signResult.Status -ne "Valid") {
                Write-Host "Failed to sign $($file.Name): $($signResult.Status)" -ForegroundColor Yellow
            } else {
                $signedCount++
            }
        }
    }
    Write-Host "Successfully signed $signedCount missing file signatures!" -ForegroundColor Green
}

# 3. Sign the installer if it exists
if (Test-Path $exePath) {
    Write-Host "Signing the installer ($exePath)..." -ForegroundColor Cyan
    $signResult = Set-AuthenticodeSignature -FilePath $exePath -Certificate $cert -HashAlgorithm SHA256

    if ($signResult.Status -eq "Valid") {
        Write-Host "Success! The installer has been digitally signed." -ForegroundColor Green
    } else {
        Write-Host "Failed to sign the installer. Status: $($signResult.Status)" -ForegroundColor Red
    }
}

Write-Host "`nDONE! Press any key to exit..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
