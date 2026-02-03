// src\KsefGateway.KsefService\Controllers\DebugController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services;
using System.Text;

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

        // 1. Конфигурация
        [HttpGet("config")]
        public async Task<IActionResult> GetCurrentConfig()
        {
            var baseUrl = await _settingsService.GetValueAsync("Ksef:BaseUrl");
            var nip = await _settingsService.GetValueAsync("Ksef:Nip");
            var idType = await _settingsService.GetValueAsync("Ksef:IdentifierType");
            
            return Ok(new 
            { 
                CurrentBaseUrl = baseUrl, 
                CurrentNip = nip,
                IdentifierType = idType, // Важно видеть, что тут 'onip'
                ServerTime = DateTime.UtcNow
            });
        }

        // 2. ПРОВЕРКА ЛОГИНА (Вместо старого InitSession)
        // Этот метод просто попытается получить токен, чтобы проверить, работает ли авторизация.
        [HttpPost("check-login")]
        public async Task<IActionResult> CheckLogin()
        {
            try
            {
                var authService = HttpContext.RequestServices.GetRequiredService<KsefAuthService>();
                
                // Просто запрашиваем токен. Если его нет, сервис сам пойдет в KSeF за новым.
                var token = await authService.GetAccessTokenAsync();

                return Ok(new 
                { 
                    Message = "✅ Auth Success!", 
                    TokenPreview = token.Substring(0, Math.Min(20, token.Length)) + "..." 
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new 
                { 
                    Error = "❌ Auth Failed", 
                    Details = ex.Message,
                    Inner = ex.InnerException?.Message
                });
            }
        }

        // 3. ОТПРАВКА ФАЙЛА (Главный метод)
        [HttpPost("send-file")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> SendInvoiceFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is empty.");

            try
            {
                // Читаем файл в строку
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
                var xmlContent = await reader.ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(xmlContent))
                    return BadRequest("File content is empty.");

                var authService = HttpContext.RequestServices.GetRequiredService<KsefAuthService>();
                
                // Вызываем метод отправки
                var resultRef = await authService.SendInvoiceDirectAsync(xmlContent);

                return Ok(new 
                { 
                    Message = "✅ Invoice Sent Successfully!", 
                    KsefReferenceNumber = resultRef,
                    FileName = file.FileName,
                    Size = file.Length
                });
            }
            catch (Exception ex)
            {
                // Полная детализация ошибки для отладки
                return BadRequest(new 
                { 
                    Error = "CRITICAL SEND ERROR", 
                    Message = ex.Message,
                    InnerException = ex.InnerException?.Message,
                    StackTrace = ex.StackTrace
                });
            }
        }
    }
}