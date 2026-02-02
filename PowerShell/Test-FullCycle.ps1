# Адрес вашего локального шлюза
$gateway = "http://localhost:5062"

Write-Host "🚀 STARTING FULL DIAGNOSTIC CYCLE..." -ForegroundColor Cyan

# 1. ПОЛУЧАЕМ КОНФИГУРАЦИЮ
Write-Host "`n[1] Checking Gateway Configuration..." -ForegroundColor Yellow
try {
    $config = Invoke-RestMethod "$gateway/api/debug/config" -ErrorAction Stop
    $ksefUrl = $config.currentBaseUrl
    Write-Host "   Gateway is targeting: $ksefUrl" -ForegroundColor White
    
    if ($config.Status -like "*WARNING*") {
        Write-Host "   ⚠️  $($config.Status)" -ForegroundColor Red
    }
} catch {
    Write-Error "Could not connect to Gateway at $gateway. Is it running?"
    exit
}

# 2. ПОЛУЧАЕМ ТОКЕН
Write-Host "`n[2] Fetching Auth Token..." -ForegroundColor Yellow
$tokenResp = Invoke-RestMethod "$gateway/api/System/token"
$token = $tokenResp.token
if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Error "Token is missing! Log in via Web UI first."
    exit
}
Write-Host "   Token received (Length: $($token.Length))" -ForegroundColor Green

# 3. ГЕНЕРИРУЕМ XML
Write-Host "`n[3] Generating Test XML..." -ForegroundColor Yellow
$xmlContent = Invoke-RestMethod "$gateway/api/debug/xml"
if ($xmlContent -notmatch "<Faktura") {
    Write-Error "Invalid XML received!"
    exit
}
$xmlBytes = [System.Text.Encoding]::UTF8.GetBytes($xmlContent)
$fileSize = $xmlBytes.Length
Write-Host "   XML Generated ($fileSize bytes)" -ForegroundColor Green

# 4. ПОДГОТОВКА ОТПРАВКИ (Хэширование)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha256.ComputeHash($xmlBytes)
$hashBase64 = [Convert]::ToBase64String($hashBytes)

$body = @{
    invoiceHash = @{
        fileSize = $fileSize
        hashSHA = @{
            algorithm = "SHA-256"
            encoding = "Base64"
            value = $hashBase64
        }
    }
    invoicePayload = @{
        type = "plain"
        invoiceBody = $xmlContent 
    }
} | ConvertTo-Json -Depth 10

# 5. ОТПРАВКА В KSeF (Напрямую, минуя воркер)
$targetEndpoint = "$ksefUrl/online/Invoice/Send"
Write-Host "`n[4] Sending to KSeF: $targetEndpoint" -ForegroundColor Cyan

$headers = @{
    "SessionToken" = $token
    "Accept" = "application/json"
    "Content-Type" = "application/json"
}

try {
    $response = Invoke-RestMethod -Uri $targetEndpoint -Method Put -Headers $headers -Body $body -ErrorAction Stop
    
    Write-Host "`n✅ SUCCESS! INVOICE ACCEPTED." -ForegroundColor Green -BackgroundColor Black
    Write-Host "   Ref: $($response.elementReferenceNumber)" -ForegroundColor Green
    Write-Host "   Code: $($response.processingCode)" -ForegroundColor Green
}
catch {
    $ex = $_.Exception
    $statusCode = 0
    if ($ex.Response) { $statusCode = $ex.Response.StatusCode.value__ }

    Write-Host "`n❌ FAILED ($statusCode)" -ForegroundColor Red -BackgroundColor Black
    
    if ($statusCode -eq 404) {
        Write-Host "   CAUSE: 404 usually means wrong Base URL." -ForegroundColor Yellow
        Write-Host "   Current URL in settings is: $ksefUrl" -ForegroundColor Yellow
        Write-Host "   Try changing it to: https://ksef-test.mf.gov.pl/api" -ForegroundColor Yellow
    }
    
    # Чтение тела ошибки
    if ($ex.Response) {
        $reader = [System.IO.StreamReader]::New($ex.Response.GetResponseStream())
        $errBody = $reader.ReadToEnd()
        Write-Host "   KSeF Response: $errBody" -ForegroundColor Red
    }
}