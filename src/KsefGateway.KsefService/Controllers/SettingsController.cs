//  src\KsefGateway.KsefService\Controllers\SettingsController.cs
using Microsoft.AspNetCore.Mvc;
using KsefGateway.KsefService.Services;

namespace KsefGateway.KsefService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SettingsController : ControllerBase
    {
        private readonly AppSettingsService _settingsService;

        public SettingsController(AppSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        [HttpGet]
        public async Task<IActionResult> GetSettings()
        {
            var settings = new
            {
                BaseUrl = await _settingsService.GetValueAsync("Ksef:BaseUrl"),
                PublicKeyUrl = await _settingsService.GetValueAsync("Ksef:PublicKeyUrl"),
                Nip = await _settingsService.GetValueAsync("Ksef:Nip"),
                IdentifierType = await _settingsService.GetValueAsync("Ksef:IdentifierType") ?? "onip", // Значение по умолчанию
                AuthToken = await _settingsService.GetValueAsync("Ksef:AuthToken")
            };

            return Ok(settings);
        }

        [HttpPost]
        public async Task<IActionResult> SaveSettings([FromBody] KsefSettingsModel model)
        {
            if (model == null) return BadRequest();

            // Сохраняем все поля в базу данных
            await _settingsService.SetValueAsync("Ksef:BaseUrl", model.BaseUrl);
            await _settingsService.SetValueAsync("Ksef:PublicKeyUrl", model.PublicKeyUrl);
            await _settingsService.SetValueAsync("Ksef:Nip", model.Nip);
            
            // !!! Важное поле, которое исправляет ошибку 21001
            await _settingsService.SetValueAsync("Ksef:IdentifierType", model.IdentifierType);
            
            await _settingsService.SetValueAsync("Ksef:AuthToken", model.AuthToken);

            return Ok(new { Message = "Settings saved successfully" });
        }
    }

    public class KsefSettingsModel
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string PublicKeyUrl { get; set; } = string.Empty;
        public string Nip { get; set; } = string.Empty;
        public string IdentifierType { get; set; } = "onip";
        public string AuthToken { get; set; } = string.Empty;
    }
}