# ==========================================
# ВХОД ЧЕРЕЗ ШЛЮЗ (KSEF-TEST)
# ==========================================
$NIP = "5423240211"
$TOKEN = "5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"

# Используем Шлюз (Gateway) - это самый стабильный адрес
$URL = "https://ksef-test.mf.gov.pl/api"

Write-Host "--- ПОДКЛЮЧЕНИЕ К $URL ---" -ForegroundColor Magenta

# 1. МАГИЯ TLS (Исправляет ошибку "Нет связи")
# KSeF требует строго TLS 1.2. Старый PowerShell по умолчанию может пробовать TLS 1.0.
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12

# 2. Отключаем проверку сертификата (на всякий случай)
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# 3. Подгружаем криптографию
Add-Type -AssemblyName System.Security

# ==========================================
# ФУНКЦИЯ ШИФРОВАНИЯ
# ==========================================
function Protect-Token {
    param($DataString, $PemString)
    # Чистка PEM
    $clean = $PemString -replace "-----.*-----", "" -replace "`r", "" -replace "`n", "" -replace " ", ""
    
    try {
        $bytes = [Convert]::FromBase64String($clean)
        $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes)
        $rsa = $cert.GetRSAPublicKey()
        $data = [System.Text.Encoding]::UTF8.GetBytes($DataString)
        # OAEP SHA256
        $pad = [System.Security.Cryptography.RSAEncryptionPadding]::OaepSHA256
        return [Convert]::ToBase64String($rsa.Encrypt($data, $pad))
    } catch {
        Write-Error "Ошибка шифрования: $_"
        throw
    }
}

# ==========================================
# ВЫПОЛНЕНИЕ
# ==========================================

# ШАГ 1: CHALLENGE
# Для Шлюза (Gateway) используем тип "onip" - это важно!
$body = @{ contextIdentifier = @{ type = "onip"; identifier = $NIP } } | ConvertTo-Json

Write-Host "[1] Запрос Challenge..." -NoNewline
try {
    $resp = Invoke-RestMethod -Uri "$URL/online/Session/AuthorisationChallenge" -Method Post -Body $body -ContentType "application/json"
    $challenge = $resp.challenge
    
    # Время (Gateway обычно возвращает timestamp, но проверим оба)
    if ($resp.timestampMs) { $ts = $resp.timestampMs } 
    else { $ts = [long](([DateTime]::Parse($resp.timestamp).ToUniversalTime() - [DateTime]::new(1970, 1, 1)).TotalMilliseconds) }
    
    Write-Host " OK! ($challenge)" -ForegroundColor Green
} catch {
    Write-Host " ОШИБКА." -ForegroundColor Red
    Write-Host "Детали: $($_.Exception.Message)" -ForegroundColor Yellow
    exit
}

# ШАГ 2: КЛЮЧ
Write-Host "[2] Скачивание ключа..." -NoNewline
try {
    $k = Invoke-RestMethod -Uri "$URL/online/General/Authorisation/PublicKey" -Method Get
    $pem = if ($k.publicKey.publicKey) { $k.publicKey.publicKey } else { $k.publicKey }
    Write-Host " OK!" -ForegroundColor Green
} catch {
    Write-Host " ОШИБКА." -ForegroundColor Red
    exit
}

# ШАГ 3: ВХОД
Write-Host "[3] Попытка входа..." -NoNewline
try {
    $enc = Protect-Token -DataString "$TOKEN|$ts" -PemString $pem
    
    $login = @{
        challenge = $challenge
        contextIdentifier = @{ type = "onip"; identifier = $NIP }
        encryptedToken = $enc
    } | ConvertTo-Json

    $res = Invoke-RestMethod -Uri "$URL/online/Session/InitToken" -Method Post -Body $login -ContentType "application/json"
    
    Write-Host "`n🔥 ПОБЕДА! СЕССИЯ ОТКРЫТА! 🔥" -ForegroundColor Green
    Write-Host "RefNum: $($res.referenceNumber)" -ForegroundColor Yellow
    Write-Host "Token:  $($res.sessionToken.token.Substring(0,15))..." -ForegroundColor Yellow
} catch {
    $stream = $_.Exception.Response.GetResponseStream()
    $msg = if ($stream) { [io.streamreader]::new($stream).ReadToEnd() } else { $_.Exception.Message }
    Write-Host "`nОШИБКА ВХОДА:" -ForegroundColor Red
    Write-Host $msg -ForegroundColor Yellow
}