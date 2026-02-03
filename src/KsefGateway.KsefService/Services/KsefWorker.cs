// src\KsefGateway.KsefService\Services\KsefWorker.cs
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KsefGateway.KsefService.Services;

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

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
        var authService = scope.ServiceProvider.GetRequiredService<KsefAuthService>();
        var ksefClient = scope.ServiceProvider.GetRequiredService<KsefClient>();

        // 1. Ищем новые фактуры
        var newInvoices = await db.OutboundInvoices
            .Where(i => i.Status == InvoiceStatus.New)
            .ToListAsync(ct);

        if (!newInvoices.Any()) return;

        // 2. Получаем сессию
        var session = await authService.GetSessionAsync();

        foreach (var inv in newInvoices)
        {
            try
            {
                // Обновляем статус на Processing
                inv.Status = InvoiceStatus.Processing;
                inv.ProcessedAt = DateTime.UtcNow;
                
                _logger.LogInformation($"Worker sending Invoice {inv.Id} ({inv.InvoiceNumber})...");

                // 3. Отправляем
                var refNum = await ksefClient.SendInvoiceAsync(inv.XmlContent, session.Token, session.ReferenceNumber);

                // Успех
                inv.Status = InvoiceStatus.SentToKsef; // Было Sent
                inv.KsefReferenceNumber = refNum;
                inv.SentAt = DateTimeOffset.UtcNow;
                
                _logger.LogInformation($"✅ Worker Sent! Ref: {refNum}");
            }
            catch (Exception ex)
            {
                // Ошибка
                inv.Status = InvoiceStatus.Rejected; // Было Error
                inv.ErrorMessage = ex.Message;
                _logger.LogError($"❌ Worker failed to send {inv.Id}: {ex.Message}");
            }
        }

        await db.SaveChangesAsync(ct);
    }
}