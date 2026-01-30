// src\KsefGateway.KsefService\Controllers\SystemController.cs
using Microsoft.AspNetCore.Mvc;
using KsefGateway.KsefService.Services;

namespace KsefGateway.KsefService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SystemController : ControllerBase
    {
        private readonly KsefAuthService _authService;

        // Внедряем наш новый умный сервис
        public SystemController(KsefAuthService authService)
        {
            _authService = authService;
        }

        // === ГЛАВНЫЙ ТЕСТ ===
        // 1. Если токен есть в базе - вернет мгновенно.
        // 2. Если нет - сам сделает вход и вернет результат.
        [HttpGet("token")]
        public async Task<IActionResult> GetToken()
        {
            try
            {
                // Замеряем время выполнения внутри контроллера (для наглядности)
                var watch = System.Diagnostics.Stopwatch.StartNew();
                
                var token = await _authService.GetAccessTokenAsync();
                
                watch.Stop();

                return Ok(new 
                { 
                    Token = token, 
                    TimeTaken = $"{watch.Elapsed.TotalSeconds:F2} sec",
                    Message = watch.Elapsed.TotalSeconds < 1.0 
                        ? "FAST! (Loaded from Database)" 
                        : "SLOW. (Performed Full Login)"
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
        
        [HttpGet("status")]
        public IActionResult Status() => Ok("Gateway is running.");
    }
}