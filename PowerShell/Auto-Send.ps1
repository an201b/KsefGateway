# Путь к вашему файлу (проверьте имя!)
$JsonPath = "openapi(3).json"

if (-not (Test-Path $JsonPath)) {
    Write-Error "Файл $JsonPath не найден! Положите его в текущую папку."
    exit
}

Write-Host "Читаем карту API ($JsonPath)..." -ForegroundColor Cyan

# Читаем JSON
$jsonContent = Get-Content -Path $JsonPath -Raw
$json = $jsonContent | ConvertFrom-Json

# Ищем пути
Write-Host "`nСПИСОК ПУТЕЙ ДЛЯ ФАКТУР:" -ForegroundColor Yellow
$json.paths.PSObject.Properties.Name | Where-Object { 
    $_ -match "invoice" -or $_ -match "send" -or $_ -match "faktur" 
} | ForEach-Object {
    $path = $_
    # Пытаемся узнать метод (PUT/POST)
    $methods = $json.paths."$path".PSObject.Properties.Name -join ", "
    Write-Host "  $path  [$methods]" -ForegroundColor Green
}

Write-Host "`n--- Конец списка ---"