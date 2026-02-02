// src\KsefGateway.KsefService\Controllers\DebugController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services;
using KsefGateway.KsefService.Models.InvoiceFa2; // Убедитесь, что InvoiceFactory здесь

namespace KsefGateway.KsefService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DebugController : ControllerBase
    {
        private readonly AppSettingsService _settingsService;
        private readonly KsefContext _db;

        public DebugController(AppSettingsService settingsService, KsefContext db)
        {
            _settingsService = settingsService;
            _db = db;
        }

        // 1. Узнать, куда смотрит шлюз прямо сейчас
        [HttpGet("config")]
        public async Task<IActionResult> GetCurrentConfig()
        {
            var baseUrl = await _settingsService.GetValueAsync("Ksef:BaseUrl");
            var nip = await _settingsService.GetValueAsync("Ksef:Nip");
            
            // Проверка на частую ошибку с /v2
            var status = "OK";
            if (!string.IsNullOrEmpty(baseUrl) && !baseUrl.EndsWith("/api"))
            {
                status = "WARNING: URL usually should end with /api for KSeF v2 (check your settings!)";
            }

            return Ok(new 
            { 
                Status = status,
                CurrentBaseUrl = baseUrl,
                CurrentNip = nip,
                ServerTime = DateTime.UtcNow
            });
        }

        // 2. Получить свежий XML для тестов
        [HttpGet("xml")]
        public async Task<IActionResult> GetTestXml()
        {
            // Берем NIP из настроек или базы
            var nip = await _settingsService.GetValueAsync("Ksef:Nip") 
                      ?? await _db.Sessions.Select(s => s.Nip).FirstOrDefaultAsync() 
                      ?? "1111111111";

            // Генерируем уникальный номер фактуры
            var invNumber = $"TEST-{DateTime.UtcNow:HHmmss}";

            // Генерируем XML через нашу фабрику
            var xmlContent = InvoiceFactory.GenerateXml(invNumber, nip, DateTime.Now);

            // Возвращаем как файл, чтобы браузер или PowerShell красиво его приняли
            return Content(xmlContent, "application/xml");
        }
    }
}