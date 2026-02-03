# ==========================================
# НАСТРОЙКИ (Используем TEST, так как он ответил вам ранее)
# ==========================================
$NIP = "5423240211"
# Убедитесь, что это токен именно для TEST среды!
$TOKEN = "5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3" 

# Этот URL сработал у вас в TestConnect.ps1 (предположительно)
$URL = "https://api-test.ksef.mf.gov.pl/v2" 

# ==========================================
# ИСПРАВЛЕННЫЙ RSA (БЕЗ НОВЫХ ФУНКЦИЙ)
# ==========================================
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

$rsaCode = @"
using System;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public class RsaHelper {
    public static string Encrypt(string data, string pem) {
        var clean = pem
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Trim();
        
        byte[] bytes = Convert.FromBase64String(clean);

        // МЫ ОСТАВИЛИ ТОЛЬКО ЭТОТ БЛОК.
        // KSeF всегда отдает сертификат, поэтому fallback не нужен.
        using (var cert = new X509Certificate2(bytes)) {
            // Этот метод работает в .NET 4.6+, который есть у вас
            using (var rsa = cert.GetRSAPublicKey()) {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                return Convert.ToBase64String(rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256));
            }
        }
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'RsaHelper').Type) {
    Add-Type -TypeDefinition $rsaCode -ReferencedAssemblies "System.Security.Cryptography.X509Certificates"
}

# ==========================================
# ВЫПОЛНЕНИЕ
# ==========================================

# 1. CHALLENGE
Write-Host "--- 1. CHALLENGE ($URL) ---" -ForegroundColor Cyan
$challengeBody = @{ contextIdentifier = @{ type = "Nip"; value = $NIP } } | ConvertTo-Json

try {
    $resp = Invoke-RestMethod -Uri "$URL/online/Session/AuthorisationChallenge" -Method Post -Body $challengeBody -ContentType "application/json"
    $challenge = $resp.challenge
    
    if ($resp.timestampMs) { $ts = $resp.timestampMs } 
    else { $ts = [long](([DateTime]::Parse($resp.timestamp).ToUniversalTime() - [DateTime]::new(1970, 1, 1)).TotalMilliseconds) }

    Write-Host "SUCCESS! Challenge: $challenge" -ForegroundColor Green
} catch {
    Write-Error "Challenge Failed. Ошибка: $($_.Exception.Message)"
    exit
}

# 2. KEY
Write-Host "`n--- 2. PUBLIC KEY ---" -ForegroundColor Cyan
try {
    $kResp = Invoke-RestMethod -Uri "$URL/online/General/Authorisation/PublicKey" -Method Get
    $pem = if ($kResp.publicKey.publicKey) { $kResp.publicKey.publicKey } else { $kResp.publicKey }
    Write-Host "SUCCESS! Key downloaded." -ForegroundColor Green
} catch {
    Write-Error "Key Failed"
    exit
}

# 3. ENCRYPT
Write-Host "`n--- 3. ENCRYPT ---" -ForegroundColor Cyan
try {
    $enc = [RsaHelper]::Encrypt("$TOKEN|$ts", $pem)
    Write-Host "Token Encrypted." -ForegroundColor Green
} catch {
    Write-Error "Encryption Error: $($_.Exception.InnerException.Message)"
    exit
}

# 4. LOGIN
Write-Host "`n--- 4. LOGIN ---" -ForegroundColor Cyan
$loginBody = @{
    challenge = $challenge
    contextIdentifier = @{ type = "Nip"; value = $NIP }
    encryptedToken = $enc
} | ConvertTo-Json

try {
    $lResp = Invoke-RestMethod -Uri "$URL/online/Session/InitToken" -Method Post -Body $loginBody -ContentType "application/json"
    Write-Host "LOGIN SUCCESS!" -ForegroundColor Green
    Write-Host "Ref: $($lResp.referenceNumber)" -ForegroundColor Yellow
    Write-Host "Token: $($lResp.sessionToken.token.Substring(0,10))..." -ForegroundColor Yellow
} catch {
    $stream = $_.Exception.Response.GetResponseStream()
    Write-Error "Login Failed: $([io.streamreader]::new($stream).ReadToEnd())"
}