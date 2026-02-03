// src\KsefGateway.KsefService\Controllers\SystemController.cs
using Microsoft.AspNetCore.Mvc;
using KsefGateway.KsefService.Services;
using System.Diagnostics;

namespace KsefGateway.KsefService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly KsefAuthService _authService;

    public SystemController(KsefAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet("token")]
    public async Task<IActionResult> GetToken()
    {
        var sw = Stopwatch.StartNew();
        
        // ИСПРАВЛЕНИЕ: GetAccessTokenAsync -> GetSessionAsync().Token
        var session = await _authService.GetSessionAsync();
        
        sw.Stop();

        return Ok(new 
        { 
            Token = session.Token, 
            TimeTaken = $"{sw.Elapsed.TotalSeconds:N2} sec",
            Message = "FAST! (Loaded from Database)" 
        });
    }
}