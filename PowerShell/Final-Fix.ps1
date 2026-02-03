# ==========================================
# KSEF API v2 - FINAL SUCCESS SCRIPT
# ==========================================
$NIP = "5423240211"
$TOKEN = "5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"

# ЕДИНСТВЕННЫЙ РАБОЧИЙ АДРЕС (ПОДТВЕРЖДЕНО ВАШИМИ ТЕСТАМИ)
$URL = "https://api-test.ksef.mf.gov.pl/v2"

Write-Host "==========================================" -ForegroundColor Magenta
Write-Host " ЦЕЛЬ: $URL" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

# 1. ОБЯЗАТЕЛЬНАЯ НАСТРОЙКА TLS 1.2
# Без этого Imperva сбрасывает соединение
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# 2. Подготовка криптографии (RSA)
Add-Type -AssemblyName System.Security

# Функция шифрования (Чистый PowerShell, без C# вставок)
function Protect-Token {
    param($Data, $Pem)
    # Очистка ключа
    $clean = $Pem -replace "-----.*-----", "" -replace "`r", "" -replace "`n", "" -replace " ", ""
    $bytes = [Convert]::FromBase64String($clean)
    
    # Создаем сертификат и шифруем
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes)
    $rsa = $cert.GetRSAPublicKey()
    $dataBytes = [System.Text.Encoding]::UTF8.GetBytes($Data)
    $encBytes = $rsa.Encrypt($dataBytes, [System.Security.Cryptography.RSAEncryptionPadding]::OaepSHA256)
    
    return [Convert]::ToBase64String($encBytes)
}

# ==========================================
# ШАГ 1: CHALLENGE (POST запрос)
# ==========================================
Write-Host "`n[1] Запрос Challenge..." -NoNewline

# ВАЖНО: Для API v2 используем "Nip" и "value"
$challengeBody = @{
    contextIdentifier = @{
        type = "Nip"
        value = $NIP
    }
} | ConvertTo-Json

try {
    # Отправляем POST (это решит ошибку 404)
    $resp1 = Invoke-RestMethod -Uri "$URL/online/Session/AuthorisationChallenge" -Method Post -Body $challengeBody -ContentType "application/json"
    
    # KSeF v2 возвращает timestampMs (число)
    if ($resp1.timestampMs) {
        $ts = $resp1.timestampMs
        $challenge = $resp1.challenge
    } else {
        # Fallback (на всякий случай)
        $challenge = $resp1.challenge
        $ts = [long](([DateTime]::Parse($resp1.timestamp).ToUniversalTime() - [DateTime]::new(1970, 1, 1)).TotalMilliseconds)
    }

    Write-Host " OK!" -ForegroundColor Green
    Write-Host "    Challenge: $challenge" -ForegroundColor Gray
} catch {
    Write-Host " ОШИБКА!" -ForegroundColor Red
    # Показываем реальную причину ошибки
    $ex = $_.Exception
    if ($ex.Response) {
        $reader = [System.IO.StreamReader]::new($ex.Response.GetResponseStream())
        Write-Host "    Ответ сервера: $($reader.ReadToEnd())" -ForegroundColor Yellow
    } else {
        Write-Host "    Ошибка сети: $($ex.Message)" -ForegroundColor Red
    }
    exit
}

# ==========================================
# ШАГ 2: КЛЮЧ
# ==========================================
Write-Host "[2] Скачивание ключа..." -NoNewline
try {
    $resp2 = Invoke-RestMethod -Uri "$URL/online/General/Authorisation/PublicKey" -Method Get
    # Иногда ключ лежит в .publicKey, иногда в .publicKey.publicKey
    $pemKey = if ($resp2.publicKey.publicKey) { $resp2.publicKey.publicKey } else { $resp2.publicKey }
    Write-Host " OK!" -ForegroundColor Green
} catch {
    Write-Error "Сбой скачивания ключа: $($_.Exception.Message)"
    exit
}

# ==========================================
# ШАГ 3: ШИФРОВАНИЕ И ВХОД
# ==========================================
Write-Host "[3] Вход в систему..." -NoNewline

try {
    # Шифруем: Токен + "|" + timestampMs
    $encrypted = Protect-Token -Data "$TOKEN|$ts" -Pem $pemKey
    
    $loginBody = @{
        challenge = $challenge
        contextIdentifier = @{
            type = "Nip"
            value = $NIP
        }
        encryptedToken = $encrypted
    } | ConvertTo-Json

    $resp3 = Invoke-RestMethod -Uri "$URL/online/Session/InitToken" -Method Post -Body $loginBody -ContentType "application/json"
    
    Write-Host " УСПЕХ! 🔥" -ForegroundColor Green
    Write-Host "`n-------------------------------------------"
    Write-Host "Session ReferenceNumber: $($resp3.referenceNumber)" -ForegroundColor Yellow
    
    $token = if ($resp3.sessionToken.token) { $resp3.sessionToken.token } else { $resp3.token }
    Write-Host "Session Token: $token" -ForegroundColor Cyan
    Write-Host "-------------------------------------------"
    
} catch {
    Write-Host " ПРОВАЛ." -ForegroundColor Red
    $ex = $_.Exception
    if ($ex.Response) {
        $reader = [System.IO.StreamReader]::new($ex.Response.GetResponseStream())
        $errBody = $reader.ReadToEnd()
        Write-Host "    KSeF Error: $errBody" -ForegroundColor Yellow
        
        if ($errBody -match "21111") {
            Write-Host "    >>> СОВЕТ: Ошибка 21111 значит, что токен не подходит к среде TEST." -ForegroundColor White
        }
    } else {
        Write-Host "    System Error: $($ex.Message)" -ForegroundColor Red
    }
}