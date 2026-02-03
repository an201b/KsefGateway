# Данные из CSV (DOZ S.A.)
$xmlContent = @"
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
  <tns:P_1>$(Get-Date -Format "yyyy-MM-dd")</tns:P_1>
  <tns:P_2>TEST-SWAGGER-$(Get-Random)</tns:P_2>
  <tns:P_13_1>2382.42</tns:P_13_1>
  <tns:P_14_1>547.96</tns:P_14_1>
  <tns:P_15>2930.38</tns:P_15>
  <tns:Adnotacje>
   <tns:P_16>2</tns:P_16><tns:P_17>2</tns:P_17><tns:P_18>2</tns:P_18><tns:P_18A>2</tns:P_18A>
   <tns:P_19>2</tns:P_19><tns:P_22>2</tns:P_22><tns:P_23>2</tns:P_23><tns:P_PMarzy>2</tns:P_PMarzy>
  </tns:Adnotacje>
  <tns:RodzajFaktury>VAT</tns:RodzajFaktury>
  <tns:FaWiersz>
   <tns:NrWierszaFa>1</tns:NrWierszaFa>
   <tns:P_7>Towary wg zestawienia</tns:P_7>
   <tns:P_8A>szt</tns:P_8A>
   <tns:P_8B>1</tns:P_8B>
   <tns:P_9A>2382.42</tns:P_9A>
   <tns:P_11>2382.42</tns:P_11>
   <tns:P_12>23</tns:P_12>
  </tns:FaWiersz>
 </tns:Fa>
</tns:Faktura>
"@

$fileName = "ready-to-send.xml"
$xmlContent | Out-File -FilePath $fileName -Encoding UTF8
Write-Host "File created: $fileName" -ForegroundColor Green