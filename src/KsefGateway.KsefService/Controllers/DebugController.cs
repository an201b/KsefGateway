// src\KsefGateway.KsefService\Controllers\DebugController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services;
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Models.InvoiceFa2; 
using System.Text;

namespace KsefGateway.KsefService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DebugController : ControllerBase
{
    private readonly KsefAuthService _authService;
    private readonly KsefClient _ksefClient;
    private readonly ILogger<DebugController> _logger;

    public DebugController(KsefAuthService authService, KsefClient ksefClient, ILogger<DebugController> logger)
    {
        _authService = authService;
        _ksefClient = ksefClient;
        _logger = logger;
    }

    [HttpPost("check-login")]
    public async Task<IActionResult> CheckLogin()
    {
        try
        {
            var session = await _authService.GetSessionAsync();
            return Ok(new 
            { 
                Message = "✅ Auth Success!", 
                TokenPreview = session.Token.Substring(0, 15) + "...",
                SessionReferenceNumber = session.ReferenceNumber // ПОКАЗЫВАЕМ ЕГО
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = ex.Message });
        }
    }

    [HttpPost("send-file")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> SendInvoiceFile(IFormFile file)
    {
        if (file == null) return BadRequest("File is empty.");

        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
            var xmlContent = await reader.ReadToEndAsync();

            _logger.LogInformation($"Sending file: {file.FileName}...");

            // 1. Получаем Сессию (Токен + RefNumber)
            var session = await _authService.GetSessionAsync();

            // 2. Отправляем (Передаем оба параметра!)
            var resultRef = await _ksefClient.SendInvoiceAsync(xmlContent, session.Token, session.ReferenceNumber);

            return Ok(new 
            { 
                Message = "✅ Invoice Sent!", 
                KsefReferenceNumber = resultRef
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { Error = "SEND ERROR", Message = ex.Message });
        }
    }
}