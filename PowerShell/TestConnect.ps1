# --- НАСТРОЙКИ ---
$NIP = "5423240211"          # Введите ваш NIP (10 цифр)
$TOKEN = "20260127-EC-2B84314000-F8E8C1CAF9-7D|nip-5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"      # Введите ваш Auth Token
$URL = "https://api-demo.ksef.mf.gov.pl/v2"




[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

Write-Host "--- 1. CHALLENGE ---" -ForegroundColor Cyan
$challengeBody = @{
    contextIdentifier = @{
        type = "Nip"     # <--- ИСПРАВЛЕНО: "Nip" вместо "onip"
        value = $NIP     # <--- ИСПРАВЛЕНО: "value" вместо "identifier" для v2
    }
} | ConvertTo-Json -Depth 5

try {
    $resp1 = Invoke-RestMethod -Uri "$URL/auth/challenge" -Method Post -Body $challengeBody -ContentType "application/json"
    $timestamp = $resp1.timestamp
    $challenge = $resp1.challenge
    Write-Host "SUCCESS! Challenge: $challenge" -ForegroundColor Green
} catch {
    Write-Error "Challenge Failed. Server said:"
    $stream = $_.Exception.Response.GetResponseStream()
    if ($stream) { [io.streamreader]::new($stream).ReadToEnd() }
    exit
}

Write-Host "`n--- 2. PUBLIC KEY ---" -ForegroundColor Cyan
try {
    $resp2 = Invoke-RestMethod -Uri "$URL/security/public-key-certificates" -Method Get
    $pemKey = $resp2[0].publicKey 
    if (-not $pemKey) { $pemKey = $resp2[0].certificate }
    Write-Host "SUCCESS! Key found." -ForegroundColor Green
} catch {
    Write-Error "Key Download Failed"
    exit
}

Write-Host "`n--- 3. ENCRYPTION ---" -ForegroundColor Cyan
$date = [DateTime]::Parse($timestamp).ToUniversalTime()
$unixTime = [long](($date - [DateTime]::new(1970, 1, 1)).TotalMilliseconds)
$msg = "$TOKEN|$unixTime"

$code = @"
using System;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public class RsaHelper {
    public static string Encrypt(string data, string pem) {
        var clean = pem.Replace("-----BEGIN PUBLIC KEY-----", "").Replace("-----END PUBLIC KEY-----", "").Replace("-----BEGIN CERTIFICATE-----", "").Replace("-----END CERTIFICATE-----", "").Replace("\n", "").Replace("\r", "").Trim();
        byte[] bytes = Convert.FromBase64String(clean);
        
        try {
            using (var cert = new X509Certificate2(bytes)) {
                var rsa = cert.GetRSAPublicKey();
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                return Convert.ToBase64String(rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256));
            }
        } catch {
             using (var rsa = RSA.Create()) {
                rsa.ImportSubjectPublicKeyInfo(bytes, out _);
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                return Convert.ToBase64String(rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256));
             }
        }
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'RsaHelper').Type) {
    Add-Type -TypeDefinition $code -ReferencedAssemblies "System.Security.Cryptography.X509Certificates"
}

try {
    $encryptedToken = [RsaHelper]::Encrypt($msg, $pemKey)
    Write-Host "Token Encrypted!" -ForegroundColor Green
} catch {
    Write-Error "Encryption Failed: $($_.Exception.Message)"
    exit
}

Write-Host "`n--- 4. INIT TOKEN ---" -ForegroundColor Cyan
# JSON СТРУКТУРА ДЛЯ API v2
$initBody = @{
    challenge = $challenge
    contextIdentifier = @{
        type = "Nip"
        value = $NIP
    }
    encryptedToken = $encryptedToken
} | ConvertTo-Json -Depth 5

Write-Host "Sending Payload..." -ForegroundColor Yellow

try {
    $resp3 = Invoke-RestMethod -Uri "$URL/auth/ksef-token" -Method Post -Body $initBody -ContentType "application/json"
    $refNum = $resp3.referenceNumber
    $sessionToken = $resp3.authenticationToken.token
    if (-not $sessionToken) { $sessionToken = $resp3.token } # Иногда поле просто 'token'

    Write-Host "SUCCESS! Session Init OK." -ForegroundColor Green
    Write-Host "RefNum: $refNum"
    Write-Host "SessionToken (Partial): $($sessionToken.Substring(0, 15))..."
} catch {
    Write-Host "INIT FAILED!" -ForegroundColor Red
    $stream = $_.Exception.Response.GetResponseStream()
    if ($stream) { 
        Write-Host "Server Error: $([io.streamreader]::new($stream).ReadToEnd())" -ForegroundColor Red
    }
    exit
}

Write-Host "`n--- 5. STATUS CHECK (Loop) ---" -ForegroundColor Cyan
$headers = @{
    "SessionToken" = $sessionToken
    "Accept" = "application/json"
}

for ($i = 1; $i -le 10; $i++) {
    Start-Sleep -Seconds 3
    Write-Host "Check $i..." -NoNewline
    
    try {
        # TRYING NEW URL: /common/status instead of /auth/status
        $statusUrl = "$URL/common/status/$refNum" 
        
        $statusResp = Invoke-RestMethod -Uri $statusUrl -Method Get -Headers $headers
        $code = $statusResp.processingCode
        $desc = $statusResp.processingDescription
        
        Write-Host " Code: $code ($desc)" -ForegroundColor Yellow
        
        if ($code -eq 200) {
            Write-Host "`n🔥 LOGIN COMPLETE! SUCCESS! 🔥" -ForegroundColor Green
            break
        }
    } catch {
        # If /common/status fails, let's see the error
        Write-Host " Error: $($_.Exception.Message)" -ForegroundColor Red
    }
}