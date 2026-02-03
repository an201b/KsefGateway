# ==========================================
# KSeF PATH FINDER (POST METHOD ONLY)  Это рабочий вариант!!!!!
# ==========================================
$NIP = "5423240211"
$Hosts = @("https://api-test.ksef.mf.gov.pl", "https://ksef-test.mf.gov.pl")

# Возможные пути (из Файла и из Документации)
$Paths = @(
    "/v2/auth/challenge",                         # Вариант из вашего файла openapi.json
    "/v2/online/Session/AuthorisationChallenge",  # Стандартный вариант v2
    "/api/online/Session/AuthorisationChallenge", # Вариант через Шлюз
    "/online/Session/AuthorisationChallenge"      # Корневой вариант
)

# Настройка безопасности
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

Write-Host "ЗАПУСК СКАНИРОВАНИЯ..." -ForegroundColor Cyan

foreach ($hostUrl in $Hosts) {
    foreach ($path in $Paths) {
        $fullUrl = "$hostUrl$path"
        
        # Определяем тип идентификатора (для старых и новых путей)
        if ($path -match "v2") {
            $body = @{ contextIdentifier = @{ type = "Nip"; value = $NIP } } | ConvertTo-Json -Depth 2
        } else {
            $body = @{ contextIdentifier = @{ type = "onip"; identifier = $NIP } } | ConvertTo-Json -Depth 2
        }

        Write-Host "Probing: $fullUrl ... " -NoNewline -ForegroundColor Gray

        try {
            # Важно: используем POST
            $resp = Invoke-RestMethod -Uri $fullUrl -Method Post -Body $body -ContentType "application/json" -TimeoutSec 5
            
            Write-Host "[OK] - НАЙДЕНО! 🔥" -ForegroundColor Green
            Write-Host "   Challenge: $($resp.challenge)" -ForegroundColor Yellow
            Write-Host "   Timestamp: $($resp.timestampMs)" -ForegroundColor Yellow
            Write-Host "`n>>> ВАШ ПРАВИЛЬНЫЙ URL: $fullUrl" -ForegroundColor Cyan
            return # Прерываем, если нашли
        } catch {
            $code = $_.Exception.Response.StatusCode
            if ($code) {
                if ($code -eq 400) {
                    # 400 это ТОЖЕ успех соединения (значит путь верный, просто данные не понравились)
                    Write-Host "[400 Bad Request] - ПУТЬ СУЩЕСТВУЕТ! (Проблема в JSON)" -ForegroundColor Yellow
                    Write-Host ">>> ВАШ ПРАВИЛЬНЫЙ URL: $fullUrl" -ForegroundColor Cyan
                } else {
                    Write-Host "[$code]" -ForegroundColor Red
                }
            } else {
                Write-Host "[Connection Error]" -ForegroundColor DarkGray
            }
        }
    }
}
Write-Host "`nСканирование завершено."