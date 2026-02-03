# ==========================================
# СКРИПТ: ФИНАЛЬНЫЙ ТЕСТ ОТПРАВКИ (DEMO v2)
# ==========================================

$gateway = "http://localhost:5062"

Write-Host "🚀 STARTING FINAL TEST (DEMO v2)..." -ForegroundColor Cyan

# 1. ПОЛУЧАЕМ НАСТРОЙКИ (Чтобы убедиться, что адрес верный)
Write-Host "`n[1] Checking Config..." -ForegroundColor Yellow
try {
    $config = Invoke-RestMethod "$gateway/api/debug/config" -ErrorAction Stop
    $ksefUrl = $config.currentBaseUrl
    
    # ПРОВЕРКА: Должен быть .../v2
    if ($ksefUrl -notmatch "/v2$") {
        Write-Warning "⚠️ Warning: URL '$ksefUrl' might be wrong. It should end with '/v2' for Demo."
    }
    Write-Host "   Target: $ksefUrl" -ForegroundColor Green
} catch {
    Write-Error "Gateway is offline! Run 'dotnet watch' first."
    exit
}

# 2. ПОЛУЧАЕМ ТОКЕН
Write-Host "`n[2] Fetching Token..." -ForegroundColor Yellow
$token = (Invoke-RestMethod "$gateway/api/System/token").token
if (-not $token) { Write-Error "No Token! Login via Web UI first."; exit }
Write-Host "   Token OK." -ForegroundColor Green

# 3. ГЕНЕРИРУЕМ XML (FA-2)
# Подставляем реальные данные (DOZ S.A.)
$invNumber = "TEST-$(Get-Random)"
$invDate   = Get-Date -Format "yyyy-MM-dd"

$ksefXml = @"
<tns:Faktura xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:tns="http://crd.gov.pl/wzor/2023/06/29/12648/" xsi:schemaLocation="http://crd.gov.pl/wzor/2023/06/29/12648/ schemat.xsd">
 <tns:Naglowek>
  <tns:KodFormularza kodSystemowy="FA (2)" wersjaSchemy="1-0E">FA</tns:KodFormularza>
  <tns:WariantFormularza>2</tns:WariantFormularza>
  <tns:DataWytworzeniaFa>$(Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")</tns:DataWytworzeniaFa>
 </tns:Naglowek>
 <tns:Podmiot1>
  <tns:DaneIdentyfikacyjne>
   <tns:NIP>5423240211</tns:NIP>
   <tns:Nazwa>ELEVITA POLAND SP.Z O.O.</tns:Nazwa>
  </tns:DaneIdentyfikacyjne>
  <tns:Adres>
   <tns:KodKraju>PL</tns:KodKraju>
   <tns:AdresL1>Mickiewicza 80/2, Bialystok</tns:AdresL1>
  </tns:Adres>
 </tns:Podmiot1>
 <tns:Podmiot2>
  <tns:DaneIdentyfikacyjne>
   <tns:NIP>8271807718</tns:NIP>
   <tns:Nazwa>DOZ SPOLKA AKCYJNA DIRECT SP.K.</tns:Nazwa>
  </tns:DaneIdentyfikacyjne>
  <tns:Adres>
   <tns:KodKraju>PL</tns:KodKraju>
   <tns:AdresL1>94406 Lodz, ul.KINGA C.GILLETTE 11</tns:AdresL1>
  </tns:Adres>
 </tns:Podmiot2>
 <tns:Fa>
  <tns:KodWaluty>PLN</tns:KodWaluty>
  <tns:P_1>$invDate</tns:P_1>
  <tns:P_2>$invNumber</tns:P_2>
  <tns:P_13_1>100.00</tns:P_13_1>
  <tns:P_14_1>23.00</tns:P_14_1>
  <tns:P_15>123.00</tns:P_15>
  <tns:Adnotacje>
   <tns:P_16>2</tns:P_16><tns:P_17>2</tns:P_17><tns:P_18>2</tns:P_18><tns:P_18A>2</tns:P_18A>
   <tns:P_19>2</tns:P_19><tns:P_22>2</tns:P_22><tns:P_23>2</tns:P_23><tns:P_PMarzy>2</tns:P_PMarzy>
  </tns:Adnotacje>
  <tns:RodzajFaktury>VAT</tns:RodzajFaktury>
  <tns:FaWiersz>
   <tns:NrWierszaFa>1</tns:NrWierszaFa>
   <tns:P_7>Test Towar</tns:P_7>
   <tns:P_8A>szt</tns:P_8A>
   <tns:P_8B>1</tns:P_8B>
   <tns:P_9A>100.00</tns:P_9A>
   <tns:P_11>100.00</tns:P_11>
   <tns:P_12>23</tns:P_12>
  </tns:FaWiersz>
 </tns:Fa>
</tns:Faktura>
"@

$xmlBytes = [System.Text.Encoding]::UTF8.GetBytes($ksefXml)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$hashBase64 = [Convert]::ToBase64String($sha256.ComputeHash($xmlBytes))
$fileSize = $xmlBytes.Length

Write-Host "`n[3] XML Generated. Size: $fileSize, Hash: $hashBase64" -ForegroundColor Gray

# 4. ОТПРАВКА (Send)
$body = @{
    invoiceHash = @{ fileSize = $fileSize; hashSHA = @{ algorithm = "SHA-256"; encoding = "Base64"; value = $hashBase64 } }
    invoicePayload = @{ type = "plain"; invoiceBody = $ksefXml }
} | ConvertTo-Json -Depth 10

$target = "$ksefUrl/online/Invoice/Send"
Write-Host "`n[4] Sending to KSeF ($target)..." -ForegroundColor Cyan

try