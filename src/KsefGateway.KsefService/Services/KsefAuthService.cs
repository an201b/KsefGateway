//src\KsefGateway.KsefService\Services\KsefAuthService.cs
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;
using KsefGateway.KsefService.Configuration;
using Microsoft.Extensions.Options;

namespace KsefGateway.KsefService.Services;

public class KsefAuthService
{
    private readonly KsefContext _context;
    private readonly KsefClient _client;
    private readonly KsefSettings _settings;
    private readonly ILogger<KsefAuthService> _logger;

    public KsefAuthService(KsefContext context, KsefClient client, IOptions<KsefSettings> settings, ILogger<KsefAuthService> logger)
    {
        _context = context;
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    // Возвращаем (Token, ReferenceNumber)
    public async Task<KsefSessionResult> GetSessionAsync()
    {
        var nip = _settings.ApiToken.Contains("|") ? _settings.ApiToken.Split('|')[0] : "5423240211";
        
        // В БД мы храним пока только токен, но для простоты запросим новый вход,
        // чтобы ГАРАНТИРОВАННО получить свежий referenceNumber
        _logger.LogInformation("Performing Login to get fresh Session & RefNumber...");
        
        return await PerformLoginAsync(nip);
    }

    private async Task<KsefSessionResult> PerformLoginAsync(string nip)
    {
        var challengeData = await _client.GetChallengeAsync(nip);
        var publicKey = await _client.GetPublicKeyAsync();
        
        var tokenToEncrypt = _settings.ApiToken; 
        var encryptedToken = _client.EncryptToken(tokenToEncrypt, challengeData.Timestamp, publicKey);

        // Теперь получаем полный объект
        var sessionResult = await _client.InitTokenAsync(nip, challengeData.Challenge, encryptedToken);
        
        // Сохраняем в базу (для истории)
        var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Nip == nip);
        if (session == null) { session = new KsefSession { Nip = nip }; _context.Sessions.Add(session); }
        session.AccessToken = sessionResult.Token;
        session.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60);
        await _context.SaveChangesAsync();

        return sessionResult;
    }
}