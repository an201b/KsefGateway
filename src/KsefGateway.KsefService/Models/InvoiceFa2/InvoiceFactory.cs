// src\KsefGateway.KsefService\Models\InvoiceFa2 \InvoiceFactory.cs.
using System.Xml.Linq;

namespace KsefGateway.KsefService.Models.InvoiceFa2
{
    public static class InvoiceFactory
    {
        // Пространство имен FA(2) - КРИТИЧЕСКИ ВАЖНО
        private static readonly XNamespace ns = "http://crd.gov.pl/wzor/2023/06/29/12648/";
        private static readonly XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        public static string GenerateXml(string invNumber, string sellerNip, DateTime date)
        {
            // Случайные суммы для теста
            var net = 100.00m;
            var vat = 23.00m;
            var gross = 123.00m;

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(ns + "Faktura",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi),
                    new XAttribute(XNamespace.Xmlns + "tns", ns), // Обязательно для KSeF
                    new XAttribute(xsi + "schemaLocation", $"{ns} schemat.xsd"),
                    
                    // 1. Заголовок (Nagłówek)
                    new XElement(ns + "Naglowek",
                        new XElement(ns + "KodFormularza", new XAttribute("kodSystemowy", "FA (2)"), new XAttribute("wersjaSchemy", "1-0E"), "FA"),
                        new XElement(ns + "WariantFormularza", "2"),
                        new XElement(ns + "DataWytworzeniaFa", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                    ),

                    // 2. Продавец (Podmiot1) - МЫ
                    new XElement(ns + "Podmiot1",
                        new XElement(ns + "DaneIdentyfikacyjne",
                            new XElement(ns + "NIP", sellerNip), // Наш NIP из настроек
                            new XElement(ns + "Nazwa", "Test Gateway Seller Sp. z o.o.")
                        ),
                        new XElement(ns + "Adres",
                            new XElement(ns + "KodKraju", "PL"),
                            new XElement(ns + "AdresL1", "ul. Testowa 1, 00-001 Warszawa")
                        )
                    ),

                    // 3. Покупатель (Podmiot2) - ТЕСТОВЫЙ
                    new XElement(ns + "Podmiot2",
                        new XElement(ns + "DaneIdentyfikacyjne",
                            new XElement(ns + "NIP", "1111111111"), // Тестовый NIP
                            new XElement(ns + "Nazwa", "Test Buyer SA")
                        ),
                        new XElement(ns + "Adres",
                            new XElement(ns + "KodKraju", "PL"),
                            new XElement(ns + "AdresL1", "ul. Kupiecka 5, 00-002 Kraków")
                        )
                    ),

                    // 4. Данные фактуры (Fa)
                    new XElement(ns + "Fa",
                        new XElement(ns + "KodWaluty", "PLN"),
                        new XElement(ns + "P_1", date.ToString("yyyy-MM-dd")), // Дата продажи
                        new XElement(ns + "P_2", invNumber),                   // Номер фактуры
                        new XElement(ns + "P_13_1", net.ToString("F2").Replace(",", ".")), // Сумма нетто 23%
                        new XElement(ns + "P_14_1", vat.ToString("F2").Replace(",", ".")), // НДС 23%
                        new XElement(ns + "P_15", gross.ToString("F2").Replace(",", ".")), // Сумма брутто
                        new XElement(ns + "Adnotacje",
                            new XElement(ns + "P_16", 2), new XElement(ns + "P_17", 2), new XElement(ns + "P_18", 2),
                            new XElement(ns + "P_18A", 2), new XElement(ns + "P_19", 2), new XElement(ns + "P_22", 2),
                            new XElement(ns + "P_23", 2), new XElement(ns + "P_PMarzy", 2)
                            // "2" в схеме KSeF означает "НЕТ/FALSE"
                        ),
                        new XElement(ns + "RodzajFaktury", "VAT"),
                        
                        // Строки фактуры (Wiersze)
                        new XElement(ns + "FaWiersz",
                            new XElement(ns + "NrWierszaFa", 1),
                            new XElement(ns + "P_7", "Usługa programistyczna"),
                            new XElement(ns + "P_8A", "szt"),
                            new XElement(ns + "P_8B", 1),
                            new XElement(ns + "P_9A", net.ToString("F2").Replace(",", ".")), // Цена нетто
                            new XElement(ns + "P_11", net.ToString("F2").Replace(",", ".")), // Стоимость нетто
                            new XElement(ns + "P_12", "23") // Ставка НДС
                        )
                    )
                )
            );

            return doc.ToString();
        }
    }
}