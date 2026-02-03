# --- НАСТРОЙКИ ---
$NIP = "5423240211"
$TOKEN = "20260127-EC-2B84314000-F8E8C1CAF9-7D|nip-5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"

# Выберите ОДИН адрес (раскомментируйте нужный):

# 1. API TEST (v2) - Скорее всего правильный для вас
#$URL = "https://api-test.ksef.mf.gov.pl/v2"

# 2. API DEMO (v2)
# $URL = "https://api-demo.ksef.mf.gov.pl/v2" 

# 3. GATEWAY TEST (Шлюз)
 $URL = "https://ksef-test.mf.gov.pl/api"


# --- [НОВОЕ] ВЫВОД АДРЕСА ПЕРЕД СТАРТОМ ---
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host " ПОДКЛЮЧЕНИЕ К: $URL" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta


[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

# --- 1. CHALLENGE ---
Write-Host "`n--- 1. CHALLENGE ---" -ForegroundColor Cyan
$challengeBody = @{
    contextIdentifier = @{
        type = "Nip" 
        value = $NIP 
    }
} | ConvertTo-Json -Depth 5

try {
    # ИСПРАВЛЕН ПУТЬ: /online/Session/... вместо /auth/...
    $resp1 = Invoke-RestMethod -Uri "$URL/online/Session/AuthorisationChallenge" -Method Post -Body $challengeBody -ContentType "application/json"
    
    $timestamp = $resp1.timestamp
    # Обработка timestampMs для v2
    if ($resp1.timestampMs) { 
        $timestamp = $resp1.timestampMs 
        $challenge = $resp1.challenge
        $timeForEncrypt = $timestamp # Для v2 используем число
    } else {
        $challenge = $resp1.challenge
        # Для v1 парсим дату
        $date = [DateTime]::Parse($resp1.timestamp).ToUniversalTime()
        $timeForEncrypt = [long](($date - [DateTime]::new(1970, 1, 1)).TotalMilliseconds)
    }

    Write-Host "SUCCESS! Challenge: $challenge" -ForegroundColor Green
} catch {
    Write-Error "Challenge Failed. Server said:"
    $stream = $_.Exception.Response.GetResponseStream()
    if ($stream) { [io.streamreader]::new($stream).ReadToEnd() }
    exit
}

# --- 2. PUBLIC KEY ---
Write-Host "`n--- 2. PUBLIC KEY ---" -ForegroundColor Cyan
try {
    # ИСПРАВЛЕН ПУТЬ
    $resp2 = Invoke-RestMethod -Uri "$URL/online/General/Authorisation/PublicKey" -Method Get
    $pemKey = $resp2.publicKey.publicKey
    if (-not $pemKey) { $pemKey = $resp2.publicKey }
    Write-Host "SUCCESS! Key found." -ForegroundColor Green
} catch {
    Write-Error "Key Download Failed"
    exit
}

# --- 3. ENCRYPTION ---
Write-Host "`n--- 3. ENCRYPTION ---" -ForegroundColor Cyan
$msg = "$TOKEN|$timeForEncrypt"

# ИСПРАВЛЕННЫЙ C# КОД (Убрали ту часть, что ломала PowerShell 5.1)
$code = @"
using System;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public class RsaHelper {
    public static string Encrypt(string data, string pem) {
        var clean = pem.Replace("-----BEGIN PUBLIC KEY-----", "")
                       .Replace("-----END PUBLIC KEY-----", "")
                       .Replace("-----BEGIN CERTIFICATE-----", "")
                       .Replace("-----END CERTIFICATE-----", "")
                       .Replace("\n", "").Replace("\r", "").Trim();
        
        byte[] bytes = Convert.FromBase64String(clean);
        
        // Используем только этот метод, он работает на старых Windows
        using (var cert = new X509Certificate2(bytes)) {
            using (var rsa = cert.GetRSAPublicKey()) {
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

# --- 4. INIT TOKEN ---
Write-Host "`n--- 4. INIT TOKEN ---" -ForegroundColor Cyan
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
    # ИСПРАВЛЕН ПУТЬ
    $resp3 = Invoke-RestMethod -Uri "$URL/online/Session/InitToken" -Method Post -Body $initBody -ContentType "application/json"
    $refNum = $resp3.referenceNumber
    $sessionToken = $resp3.sessionToken.token
    if (-not $sessionToken) { $sessionToken = $resp3.token }

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