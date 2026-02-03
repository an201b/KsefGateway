# ==========================================
# 1. НАСТРОЙКИ
# ==========================================
$NIP = "5423240211"

# Вставьте ваш токен
$TOKEN = "5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"

# Адрес API (Test)
$URL = "https://api-test.ksef.mf.gov.pl/v2"

Write-Host "--- ЦЕЛЬ: $URL ---" -ForegroundColor Magenta

# ==========================================
# 2. НАСТРОЙКА БЕЗОПАСНОСТИ
# ==========================================
# Включаем TLS 1.2
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
# Отключаем проверку сертификатов
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}
# Загружаем криптографию
Add-Type -AssemblyName System.Security

# ==========================================
# 3. ФУНКЦИЯ ШИФРОВАНИЯ (БЕЗ C#)
# ==========================================
function Protect-Token {
    param($DataString, $PemString)

    # Чистим ключ от лишних заголовков
    $cleanPem = $PemString.Replace("-----BEGIN PUBLIC KEY-----", "")
    $cleanPem = $cleanPem.Replace("-----END PUBLIC KEY-----", "")
    $cleanPem = $cleanPem.Replace("-----BEGIN CERTIFICATE-----", "")
    $cleanPem = $cleanPem.Replace("-----END CERTIFICATE-----", "")
    $cleanPem = $cleanPem.Replace("`r", "").Replace("`n", "").Trim()

    try {
        $keyBytes = [Convert]::FromBase64String($cleanPem)
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($keyBytes)
        
        # Получаем RSA (этот метод есть в .NET 4.6+)
        $rsa = $cert.GetRSAPublicKey()
        
        $dataBytes = [System.Text.Encoding]::UTF8.GetBytes($DataString)
        $padding = [System.Security.Cryptography.RSAEncryptionPadding]::OaepSHA256
        
        $encryptedBytes = $rsa.Encrypt($dataBytes, $padding)
        return [Convert]::ToBase64String($encryptedBytes)
    }
    catch {
        Write-Error "Ошибка шифрования внутри функции: $_"
        throw
    }
}

# ==========================================
# 4. ВЫПОЛНЕНИЕ
# ==========================================

# --- ШАГ 1: CHALLENGE ---
Write-Host "`n[1] Запрос Challenge..." -ForegroundColor Cyan
$challengeBody = @{
    contextIdentifier = @{
        type = "Nip"
        value = $NIP
    }
} | ConvertTo-Json

try {
    $resp = Invoke-RestMethod -Uri "$URL/online/Session/AuthorisationChallenge" -Method Post -Body $challengeBody -ContentType "application/json"
    $challenge = $resp.challenge
    
    # Берем время
    if ($resp.timestampMs) { 
        $ts = $resp.timestampMs 
    } else {
        $date = [DateTime]::Parse($resp.timestamp).ToUniversalTime()
        $ts = [long](($date - [DateTime]::new(1970, 1, 1)).TotalMilliseconds)
    }
    Write-Host "OK! Challenge: $challenge" -ForegroundColor Green
} catch {
    Write-Error "ОШИБКА CHALLENGE. Сервер не ответил."
    exit
}

# --- ШАГ 2: КЛЮЧ ---
Write-Host "`n[2] Скачивание ключа..." -ForegroundColor Cyan
try {
    $kResp = Invoke-RestMethod -Uri "$URL/online/General/Authorisation/PublicKey" -Method Get
    # Проверка структуры ответа
    if ($kResp.publicKey.publicKey) { 
        $pem = $kResp.publicKey.publicKey 
    } else { 
        $pem = $kResp.publicKey 
    }
    Write-Host "OK! Ключ скачан." -ForegroundColor Green
} catch {
    Write-Error "ОШИБКА ПОЛУЧЕНИЯ КЛЮЧА"
    exit
}

# --- ШАГ 3: ШИФРОВАНИЕ ---
Write-Host "`n[3] Шифрование..." -ForegroundColor Cyan
try {
    $msg = "$TOKEN|$ts"
    $encryptedToken = Protect-Token -DataString $msg -PemString $pem
    Write-Host "OK! Токен зашифрован." -ForegroundColor Green
} catch {
    Write-Error "Сбой шифрования."
    exit
}

# --- ШАГ 4: ВХОД ---
Write-Host "`n[4] Попытка входа..." -ForegroundColor Cyan
$loginBody = @{
    challenge = $challenge
    contextIdentifier = @{
        type = "Nip"
        value = $NIP
    }
    encryptedToken = $encryptedToken
} | Convert