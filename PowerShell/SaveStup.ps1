# Настройки
$settings = @{
    baseUrl = "https://api-test.ksef.mf.gov.pl/v2"
    publicKeyUrl = "https://api-test.ksef.mf.gov.pl/v2/security/public-key-certificates"
    nip = "5423240211"
    identifierType = "onip"  # <--- ВОТ ГЛАВНОЕ ИСПРАВЛЕНИЕ
    authToken = "20260127-EC-2B84314000-F8E8C1CAF9-7D|nip-5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3"
}

# Превращаем в JSON
$json = $settings | ConvertTo-Json

# Отправляем в ваш SettingsController
try {
    $response = Invoke-RestMethod -Method Post -Uri "http://localhost:5062/api/Settings" -Body $json -ContentType "application/json"
    Write-Host "✅ НАСТРОЙКИ ОБНОВЛЕНЫ УСПЕШНО!" -ForegroundColor Green
    Write-Host "Теперь IdentifierType = ONIP" -ForegroundColor Yellow
} catch {
    Write-Host "❌ Ошибка обновления настроек" -ForegroundColor Red
    Write-Host $_.Exception.Message
}