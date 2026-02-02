# ==========================================
# СКРИПТ: ОТПРАВКА РЕАЛЬНОЙ ФАКТУРЫ (DOZ S.A.)
# ==========================================

$gateway = "http://localhost:5062"

Write-Host "🚀 STARTING REAL DATA TEST (FROM CSV)..." -ForegroundColor Cyan

# 1. ДАННЫЕ ИЗ ФАЙЛА TDSheet.csv
$invNumber  = "ZSPO26000085"
$invDate    = "2026-01-28"
$sellerNip  = "5423240211"
$buyerNip   = "8271807718"  # Теперь у нас есть реальный покупатель!
$buyerName  = "DOZ SPÓŁKA AKCYJNA DIRECT SP.K."
$netSum     = "2382.42"
$vatSum     = "547.96"
$grossSum   = "2930.38"

Write-Host "   Invoice: $invNumber ($invDate)" -ForegroundColor Gray
Write-Host "   Buyer:   $buyerName (NIP: $buyerNip)" -ForegroundColor Gray
Write-Host "   Amount:  $grossSum PLN" -ForegroundColor Gray

# 2. ПОЛУЧАЕМ НАСТРОЙКИ ШЛЮЗА
try {
    $config = Invoke-RestMethod "$gateway/api/debug/config" -ErrorAction Stop
    $ksefUrl = $config.currentBaseUrl
    Write-Host "   Target:  $ksefUrl" -ForegroundColor Yellow
} catch {
    Write-Error "Gateway is offline! Run 'dotnet watch' first."
    exit
}

# 3. ПОЛУЧАЕМ ТОКЕН
$token = (Invoke-RestMethod "$gateway/api/System/token").token
if (-not $token) { Write-Error "No Token! Please login via Web UI."; exit }

# 4. ГЕНЕРИРУЕМ XML (FA-2)
# Подставляем реальные данные в структуру KSeF
$ksefXml = @"
<tns:Faktura xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:tns="http://crd.gov.pl/wzor/2023/06/29/12648/" xsi:schemaLocation="http://crd.gov.pl/wzor/2023/06/29/12648/ schemat.xsd">
 <tns:Naglowek>
  <tns:KodFormularza kodSystemowy="FA (2)" wersjaSchemy="1-0E">FA</tns:KodFormularza>
  <tns:WariantFormularza>2</tns:WariantFormularza>
  <tns:DataWytworzeniaFa>$(Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")</tns:DataWytworzeniaFa>
 </tns:Naglowek>
 <tns:Podmiot1>
  <tns:DaneIdentyfikacyjne>
   <tns:NIP>$sellerNip</tns:NIP>
   <tns:Nazwa>ELEVITA POLAND SP.Z O.O.</tns:Nazwa>
  </tns:DaneIdentyfikacyjne>
  <tns:Adres>
   <tns:KodKraju>PL</tns:KodKraju>
   <tns:AdresL1>Mickiewicza 80/2, Bialystok</tns:AdresL1>
  </tns:Adres>
 </tns:Podmiot1>
 <tns:Podmiot2>
  <tns:DaneIdentyfikacyjne>
   <tns:NIP>$buyerNip</tns:NIP>
   <tns:Nazwa>$buyerName</tns:Nazwa>
  </tns:DaneIdentyfikacyjne>
  <tns:Adres>
   <tns:KodKraju>PL</tns:KodKraju>
   <tns:AdresL1>94406 Łódź, ul.KINGA C.GILLETTE 11</tns:AdresL1>
  </tns:Adres>
 </tns:Podmiot2>
 <tns:Fa>
  <tns:KodWaluty>PLN</tns:KodWaluty>
  <tns:P_1>$invDate</tns:P_1>
  <tns:P_2>$invNumber</tns:P_2>
  <tns:P_13_1>$($netSum.Replace(',', '.'))</tns:P_13_1>
  <tns:P_14_1>$($vatSum.Replace(',', '.'))</tns:P_14_1>
  <tns:P_15>$($grossSum.Replace(',', '.'))</tns:P_15>
  <tns:Adnotacje>
   <tns:P_16>2</tns:P_16><tns:P_17>2</tns:P_17><tns:P_18>2</tns:P_18><tns:P_18A>2</tns:P_18A>
   <tns:P_19>2</tns:P_19><tns:P_22>2</tns:P_22><tns:P_23>2</tns:P_23><tns:P_PMarzy>2</tns:P_PMarzy>
  </tns:Adnotacje>
  <tns:RodzajFaktury>VAT</tns:RodzajFaktury>
  <tns:FaWiersz>
   <tns:NrWierszaFa>1</tns:NrWierszaFa>
   <tns:P_7>Towary wg zestawienia (import CSV)</tns:P_7>
   <tns:P_8A>szt</tns:P_8A>
   <tns:P_8B>1</tns:P_8B>
   <tns:P_9A>$($netSum.Replace(',', '.'))</tns:P_9A>
   <tns:P_11>$($netSum.Replace(',', '.'))</tns:P_11>
   <tns:P_12>23</tns:P_12>
  </tns:FaWiersz>
 </tns:Fa>
</tns:Faktura>
"@

# 5. ОТПРАВКА (Хэширование и PUT)
$xmlBytes = [System.Text.Encoding]::UTF8.GetBytes($ksefXml)
$sha256   = [System.Security.Cryptography.SHA256]::Create()
$hash     = [Convert]::ToBase64String($sha256.ComputeHash($xmlBytes))

$body = @{
    invoiceHash = @{ fileSize = $xmlBytes.Length; hashSHA = @{ algorithm = "SHA-256"; encoding = "Base64"; value = $hash } }
    invoicePayload = @{ type = "plain"; invoiceBody = $ksefXml }
} | ConvertTo-Json -Depth 10

$target = "$ksefUrl/online/Invoice/Send"
Write-Host "`nSending to KSeF ($target)..." -ForegroundColor Cyan

try {
    $resp = Invoke-RestMethod -Uri $target -Method Put -Headers @{ "SessionToken"=$token; "Content-Type"="application/json" } -Body $body -ErrorAction Stop
    
    Write-Host "`n✅ SUCCESS! INVOICE SENT." -ForegroundColor Green -BackgroundColor Black
    Write-Host "   KSeF Reference: $($resp.elementReferenceNumber)" -ForegroundColor Green
    Write-Host "   Processing Code: $($resp.processingCode)" -ForegroundColor Green
} catch {
    Write-Host "`n❌ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.Exception.Response) {
        $reader = [System.IO.StreamReader]::New($_.Exception.Response.GetResponseStream())
        Write-Host "   KSeF Details: $($reader.ReadToEnd())" -ForegroundColor Yellow
    }
}