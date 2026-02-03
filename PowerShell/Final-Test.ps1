# ==========================================
# 1. НАСТРОЙКИ
# ==========================================
$NIP = "5423240211"
# Токен. Убедитесь, что он соответствует среде (Test или Demo)
$TOKEN = "5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"

# Адрес. Возвращаемся на api-test, так как ksef-test у вас отвалился по соединению.
# Если api-test не сработает, поменяйте в строке ниже "test" на "demo"
$ENV_HOST = "api-test" 
$URL = "https://$ENV_HOST.ksef.mf.gov.pl/v2"

Write-Host "--- ЦЕЛЬ: $URL ---" -ForegroundColor Magenta

# ==========================================
# 2. НАСТРОЙКА БЕЗОПАСНОСТИ (ТАНК)
# ==========================================
# Включаем TLS 1.2 (Обязательно для KSeF)
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
# Отключаем проверку SSL сертификатов (чтобы антивирус не мешал)
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

# Подгружаем системную библиотеку безопасности (без компиляции!)
Add-Type -AssemblyName System.Security

# ==========================================
# 3. ФУНКЦИЯ ШИФРОВАНИЯ (ЧИСТЫЙ PS)
# ==========================================
function Protect-Token {
    param($DataString, $PemString)

    # 1. Очистка PEM (удаляем заголовки)
    $cleanPem = $PemString -replace "-----BEGIN PUBLIC KEY-----", "" `
                           -replace "-----END PUBLIC KEY-----", "" `
                           -replace "-----BEGIN CERTIFICATE-----", "" `
                           -replace "-----END CERTIFICATE-----", "" `
                           -replace "`r", "" `
                           -replace "`n", "" 
    $cleanPem = $cleanPem.Trim()

    # 2. Создаем объект сертификата из байтов
    $keyBytes = [Convert]::FromBase64String($cleanPem)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($keyBytes)

    # 3. Получаем RSA провайдер
    $rsa = $cert.GetRSAPublicKey()
    
    # 4. Шифруем (OAEP SHA256)
    $dataBytes = [System.Text.Encoding]::UTF8.GetBytes($DataString)
    $padding = [System.Security.Cryptography.RSAEncryptionPadding]::OaepSHA256
    $encryptedBytes = $rsa.Encrypt($dataBytes, $padding)

    return [Convert]::ToBase64String($encryptedBytes)
}

# ==========================================
# 4. ВЫПОЛНЕНИЕ
# ==========================================

# --- ШАГ 1: CHALLENGE ---
Write-Host "`n[1] Запрос Challenge..." -ForegroundColor Cyan

# Для API v2 используем Nip и value
$challengeBody = @{
    contextIdentifier = @{
        type = "Nip"
        value = $NIP
    }
} | ConvertTo-Json

try {
    $resp = Invoke-RestMethod -Uri "$URL/online/Session/AuthorisationChallenge" -Method Post -Body $challengeBody -ContentType "application/json"
    $challenge = $resp.challenge
    
    # Обработка времени
    if ($resp.timestampMs) { 
        $ts = $resp.timestampMs 
    } else { 
        # На случай если ответит старый API
        $date = [DateTime]::Parse($resp.timestamp).ToUniversalTime()
        $ts = [long](($date - [DateTime]::new(1970, 1, 1)).TotalMilliseconds)
    }

    Write-Host "OK! Challenge: $challenge" -ForegroundColor Green
} catch {
    Write-Error "ОШИБКА СЕТИ (Challenge). Сервер не ответил."
    Write-Host "Детали: $($_.Exception.Message)" -ForegroundColor Red
    # Пробуем вывести тело ответа, если сервер его прислал
    $stream = $_.Exception.Response.GetResponseStream()
    if ($stream) { Write-Host "Ответ сервера: $([io.streamreader]::new($stream).ReadToEnd())" -ForegroundColor DarkRed }
    exit
}

# --- ШАГ 2: КЛЮЧ ---
Write-Host "`n[2] Скачивание ключа..." -ForegroundColor Cyan
try {
    $kResp = Invoke-RestMethod -Uri "$URL/online/General/Authorisation/PublicKey" -Method Get
    $pem = if ($kResp.publicKey.publicKey) { $kResp.publicKey.publicKey } else { $kResp.publicKey }
    Write-Host "OK! Ключ есть." -ForegroundColor Green
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
    Write-Error "ОШИБКА ВНУТРИ POWERSHELL ПРИ ШИФРОВАНИИ"
    Write-Host "Детали: $($_.Exception.Message)" -ForegroundColor Red
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
} | ConvertTo-Json

try {
    $lResp = Invoke-RestMethod -Uri "$URL/online/Session/InitToken" -Method Post -Body $loginBody -ContentType "application/json"
    
    Write-Host "🔥 УСПЕШНЫЙ ВХОД! (HTTP 201) 🔥" -ForegroundColor Green
    Write-Host "ReferenceNumber: $($lResp.referenceNumber)" -ForegroundColor Yellow
    
    # Обработка разных форматов ответа токена
    $finalToken = if ($lResp.sessionToken.token) { $lResp.sessionToken.token } else { $lResp.token }
    Write-Host "SessionToken: $($finalToken.Substring(0,15))..." -ForegroundColor Yellow
    
} catch {
    $stream = $_.Exception.Response.GetResponseStream()
    $errText = if ($stream) { [io.streamreader]::new($stream).ReadToEnd() } else { "Нет деталей" }
    
    Write-Host "ОШИБКА АВТОРИЗАЦИИ (InitToken)!" -ForegroundColor Red
    Write-Host "Ответ сервера: $errText" -ForegroundColor Yellow
    
    if ($errText -