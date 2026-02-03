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

        [HttpGet("{key}")]
        public async Task<IActionResult> Get(string key)
        {
            var val = await _settingsService.GetValueAsync(key);
            return Ok(new { Key = key, Value = val });
        }

        [HttpPost]
        public async Task<IActionResult> Set([FromBody] SettingDto dto)
        {
            await _settingsService.SetValueAsync(dto.Key, dto.Value);
            return Ok(new { Message = "Setting updated. Hot reload active." });
        }

        //public class SettingDto { public string Key { get; set; } public string Value { get; set; } }
        public class SettingDto 
        { 
            public string Key { get; set; } = string.Empty; 
            public string Value { get; set; } = string.Empty; 
        }
    }
}