using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace KsefGateway.KsefService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly KsefSettings _settings;
    private readonly KsefClient _ksefClient;

    public SystemController(
        IOptions<KsefSettings> settings,
        KsefClient ksefClient)
    {
        _settings = settings.Value;
        _ksefClient = ksefClient;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Online",
            Service = "KSeF Gateway",
            KsefEnvironment = _settings.Environment,
            KsefUrl = _settings.BaseUrl
        });
    }

    /// <summary>
    /// Проверка связи с KSeF (получение AuthorisationChallenge)
    /// </summary>
    [HttpGet("test-connection")]
    public async Task<IActionResult> TestKsefConnection(
        [FromQuery] string nip,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nip))
        {
            return BadRequest(new
            {
                Error = "NIP is required",
                Example = "/api/system/test-connection?nip=5423240211"
            });
        }

        try
        {
            var challenge = await _ksefClient
                .GetAuthorisationChallengeAsync(nip, cancellationToken);

            return Ok(new
            {
                Message = "Успешное соединение с KSeF",
                ServerTimestampMs = challenge.Timestamp,
                Challenge = challenge.Challenge
            });
        }
        catch (Exception ex)
        {
            // Собираем полную цепочку исключений
            var fullErrorMessage = ex.Message;
            var inner = ex.InnerException;

            while (inner != null)
            {
                fullErrorMessage += " ---> " + inner.Message;
                inner = inner.InnerException;
            }

            return BadRequest(new
            {
                Error = "Ошибка соединения с KSeF",
                Details = fullErrorMessage,
                ExceptionType = ex.GetType().Name
            });
        }
    }
}
