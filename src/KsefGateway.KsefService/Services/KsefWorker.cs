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

                await Task.Delay(5000, stoppingToken);
            }
        }

        private async Task ProcessQueueAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
            var authService = scope.ServiceProvider.GetRequiredService<KsefAuthService>();
            var settingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
            var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

            // 1. Берем "сырые" данные (SQLite fix)
            var rawInvoices = await db.OutboundInvoices
                .Where(i => i.Status == InvoiceStatus.New)
                .Take(50)
                .ToListAsync(ct);

            if (!rawInvoices.Any()) return;

            // 2. Сортируем в памяти
            var invoices = rawInvoices.OrderBy(i => i.CreatedAt).Take(5).ToList();

            _logger.LogInformation($"Found {invoices.Count} invoices to send.");

            // 3. Получаем токен
            string token;
            try 
            {
                token = await authService.GetAccessTokenAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Auth failed: {ex.Message}");
                return;
            }

            // 4. Настраиваем клиента
            var client = httpClientFactory.CreateClient();
            var baseUrl = (await settingsService.GetValueAsync("Ksef:BaseUrl")).TrimEnd('/');
            
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // KSeF требует SessionToken в заголовке
            if (client.DefaultRequestHeaders.Contains("SessionToken")) client.DefaultRequestHeaders.Remove("SessionToken");
            client.DefaultRequestHeaders.Add("SessionToken", token);

foreach (var invoice in invoices)
            {
                try
                {
                    _logger.LogInformation($"Sending Invoice {invoice.InvoiceNumber}...");

                    // 1. Готовим байты XML
                    var xmlBytes = Encoding.UTF8.GetBytes(invoice.XmlContent);
                    
                    // 2. Считаем ХЭШ (SHA-256)
                    using var sha = SHA256.Create();
                    var hashBytes = sha.ComputeHash(xmlBytes);
                    var hashString = Convert.ToBase64String(hashBytes);

                    // 3. Формируем тело запроса (Строго по схеме SendInvoiceRequest)
                    var payload = new
                    {
                        invoiceHash = new 
                        { 
                            fileSize = xmlBytes.Length,
                            hashSHA = new 
                            { 
                                algorithm = "SHA-256", 
                                encoding = "Base64", 
                                value = hashString 
                            } 
                        },
                        invoicePayload = new 
                        { 
                            type = "plain", 
                            invoiceBody = invoice.XmlContent 
                        }
                    };
                    var fullUrl = $"{baseUrl}/online/Invoice/Send"; // Собираем URL в переменную
                    // --- ДОБАВЬТЕ ЭТУ СТРОКУ: ---
                    _logger.LogInformation($"[DEBUG] Sending PUT to: {fullUrl}"); 
                    // ----------------------------
                    // 4. ОТПРАВКА (ИСПРАВЛЕН URL)
                    // Было: /online/invoices/send (404 Error)
                    // Стало: /online/Invoice/Send (Правильно)
                    var response = await client.PutAsJsonAsync(
                        $"{baseUrl}/online/Invoice/Send", 
                        payload, 
                        ct
                    );
                    
                    var respContent = await response.Content.ReadAsStringAsync(ct);

                    if (response.IsSuccessStatusCode)
                    {
                        // УСПЕХ
                        var doc = JsonDocument.Parse(respContent);
                        var ksefRef = doc.RootElement.GetProperty("elementReferenceNumber").GetString();
                        
                        invoice.Status = InvoiceStatus.Success;
                        invoice.KsefReferenceNumber = ksefRef;
                        invoice.ProcessedAt = DateTime.UtcNow;
                        invoice.ErrorMessage = null;
                        
                        _logger.LogInformation($"✅ SUCCESS! Sent {invoice.InvoiceNumber}. KSeF Ref: {ksefRef}");
                    }
                    else
                    {
                        // ОШИБКА
                        _logger.LogError($"--> REJECTED [HTTP {response.StatusCode}]: {respContent}");

                        string errorMsg = $"HTTP {(int)response.StatusCode}";
                        try 
                        {
                            var errDoc = JsonDocument.Parse(respContent);
                            if(errDoc.RootElement.TryGetProperty("exception", out var exElem))
                            {
                                var detailList = exElem.GetProperty("exceptionDetailList");
                                errorMsg = detailList[0].GetProperty("exceptionDescription").GetString()!;
                            }
                        }
                        catch {}

                        invoice.Status = InvoiceStatus.Rejected;
                        invoice.ErrorMessage = errorMsg;
                        invoice.ProcessedAt = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"--> EXCEPTION processing invoice {invoice.InvoiceNumber}");
                    invoice.Status = InvoiceStatus.Rejected;
                    invoice.ErrorMessage = $"Internal: {ex.Message}";
                    invoice.ProcessedAt = DateTime.UtcNow;
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }
}