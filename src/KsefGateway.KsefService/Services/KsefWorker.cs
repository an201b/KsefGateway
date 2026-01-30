// src\KsefGateway.KsefService\Services\KsefWorker.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;
using System.Security.Cryptography;

namespace KsefGateway.KsefService.Services
{
    public class KsefWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<KsefWorker> _logger;

        public KsefWorker(IServiceProvider serviceProvider, ILogger<KsefWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(">>> KsefWorker (Courier) started. Waiting for invoices...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessQueueAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error in Worker loop");
                }

                // Ждем 10 секунд перед следующей проверкой
                await Task.Delay(10000, stoppingToken);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            // Worker - это Singleton, а DbContext - Scoped. Создаем область видимости вручную.
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
            var authService = scope.ServiceProvider.GetRequiredService<KsefAuthService>();
            var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

            // 1. Ищем новые фактуры
            // ИСПРАВЛЕНИЕ: SQLite не умеет делать ORDER BY DateTimeOffset внутри SQL.
            // Поэтому мы берем пачку (например, 20 штук) в порядке вставки (по умолчанию),
            // а точную сортировку делаем уже в памяти приложения (Client-side evaluation).
            var rawInvoices = await db.OutboundInvoices
                .Where(i => i.Status == InvoiceStatus.New)
                .Take(20) // Берем с запасом
                .ToListAsync(ct);

            if (!rawInvoices.Any()) return; // Очередь пуста

            // Сортируем в памяти (C# делает это отлично) и берем 5 самых старых
            var invoices = rawInvoices
                .OrderBy(i => i.CreatedAt)
                .Take(5)
                .ToList();
            _logger.LogInformation($"Found {invoices.Count} invoices to send.");

            // 2. Получаем токен (Прозрачная авторизация через ваш сервис)
            string token;
            try 
            {
                token = await authService.GetAccessTokenAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Auth failed, skipping cycle: {ex.Message}");
                return;
            }

            // 3. Готовим клиента
            var client = httpClientFactory.CreateClient();
            var baseUrl = (await settingsService.GetValueAsync("Ksef:BaseUrl")).TrimEnd('/');
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 4. Отправляем каждую фактуру
            foreach (var invoice in invoices)
            {
                try
                {
                    _logger.LogInformation($"Sending Invoice {invoice.InvoiceNumber}...");

                    // --- ХЭШИРОВАНИЕ (Требование KSeF) ---
                    var xmlBytes = Encoding.UTF8.GetBytes(invoice.XmlContent);
                    using var sha = SHA256.Create();
                    var hashBytes = sha.ComputeHash(xmlBytes);
                    var hashString = Convert.ToBase64String(hashBytes);

                    // Формируем тело запроса
                    var sendBody = new
                    {
                        invoiceHash = new { hashSHA = new { algorithm = "SHA-256", encoding = "Base64", value = hashString } },
                        invoicePayload = new { type = "plain", invoiceBody = invoice.XmlContent }
                    };

                    // Отправка (PUT)
                    var response = await client.PutAsync(
                        $"{baseUrl}/online/Invoice/Send",
                        new StringContent(JsonSerializer.Serialize(sendBody), Encoding.UTF8, "application/json"),
                        ct
                    );

                    var respContent = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        // УСПЕХ
                        var doc = JsonDocument.Parse(respContent);
                        var refNum = doc.RootElement.GetProperty("referenceNumber").GetString();
                        
                        invoice.Status = InvoiceStatus.SentToKsef; // Меняем статус
                        invoice.KsefReferenceNumber = refNum;
                        invoice.SentAt = DateTimeOffset.UtcNow;
                        invoice.ErrorMessage = null;
                        
                        _logger.LogInformation($"--> SUCCESS! Ref: {refNum}");
                    }
                    else
                    {
                        // ОШИБКА KSeF (валидация XML и т.д.)
                        invoice.Status = InvoiceStatus.Rejected;
                        invoice.ErrorMessage = respContent;
                        _logger.LogError($"--> REJECTED: {respContent}");
                    }
                }
                catch (Exception ex)
                {
                    // Внутренняя ошибка (сеть и т.д.)
                    invoice.ErrorMessage = $"Internal Error: {ex.Message}";
                    _logger.LogError($"--> EXCEPTION: {ex.Message}");
                }

                // Сохраняем изменения в базе
                await db.SaveChangesAsync(ct);
            }
        }
    }
}