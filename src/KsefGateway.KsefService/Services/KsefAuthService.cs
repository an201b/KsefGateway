//  src\KsefGateway.KsefService\Services\KsefAuthService.cs
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;

namespace KsefGateway.KsefService.Services
{
    public class KsefAuthService
    {
        private readonly KsefContext _context;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppSettingsService _settingsService;
        private readonly ILogger<KsefAuthService> _logger;

        public KsefAuthService(
            KsefContext context,
            IHttpClientFactory httpClientFactory,
            AppSettingsService settingsService,
            ILogger<KsefAuthService> logger)
        {
            _context = context;
            _httpClientFactory = httpClientFactory;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            var nip = await _settingsService.GetValueAsync("Ksef:Nip");
            if (string.IsNullOrEmpty(nip)) throw new Exception("NIP not configured.");

            var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Nip == nip);

            if (session != null && session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return session.AccessToken;
            }

            _logger.LogInformation("Session expired or missing. Performing Full Login (API v2)...");

            var (newToken, newRefToken, expires) = await PerformFullLoginAsync(nip);

            if (session == null)
            {
                session = new KsefSession { Nip = nip };
                _context.Sessions.Add(session);
            }

            session.AccessToken = newToken;
            session.RefreshToken = newRefToken;
            session.AccessTokenExpiresAt = expires;
            
            await _context.SaveChangesAsync();
            _logger.LogInformation("Login Successful! Token saved.");

            return newToken;
        }

private async Task<(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)> PerformFullLoginAsync(string nip)
        {
            var baseUrl = (await _settingsService.GetValueAsync("Ksef:BaseUrl")).TrimEnd('/');
            // Читаем URL ключа из настроек
            var keyUrlSetting = await _settingsService.GetValueAsync("Ksef:PublicKeyUrl");
            // Читаем тип идентификатора (по умолчанию Nip)
            var idType = await _settingsService.GetValueAsync("Ksef:IdentifierType") ?? "Nip";
            
            var authToken = (await _settingsService.GetValueAsync("Ksef:AuthToken")).Trim();

            if (string.IsNullOrEmpty(authToken)) throw new Exception("AuthToken not configured.");

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // --- ШАГ 1: Challenge ---
            var challengeUrl = $"{baseUrl}/auth/challenge";
            var challengeBody = new 
            { 
                contextIdentifier = new { type = idType, value = nip } // Используем idType из настроек
            };

            _logger.LogInformation($"POST {challengeUrl}");
            var challengeResp = await client.PostAsync(challengeUrl, 
                new StringContent(JsonSerializer.Serialize(challengeBody), Encoding.UTF8, "application/json"));
            
            var challengeContent = await challengeResp.Content.ReadAsStringAsync();
            if (!challengeResp.IsSuccessStatusCode)
                 throw new Exception($"Challenge Error: {challengeContent}");

            var challengeDoc = JsonDocument.Parse(challengeContent);
            var timestampStr = challengeDoc.RootElement.GetProperty("timestamp").GetString(); 
            var challenge = challengeDoc.RootElement.GetProperty("challenge").GetString();

            var time = DateTimeOffset.Parse(timestampStr!).ToUnixTimeMilliseconds();

            // --- ШАГ 2: Public Key ---
            string pemKey = "";
            var potentialKeyUrls = new List<string>();
            
            // Если в настройках есть URL, ставим его первым приоритетом
            if (!string.IsNullOrEmpty(keyUrlSetting))
            {
                potentialKeyUrls.Add(keyUrlSetting);
            }
            
            // Добавляем дефолтные на всякий случай
            potentialKeyUrls.Add($"{baseUrl}/security/public-key-certificates");
            potentialKeyUrls.Add("https://ksef-demo.mf.gov.pl/api/security/public-key-certificates"); // Demo Fallback
            foreach (var url in potentialKeyUrls)
            {
                try 
                {
                    _logger.LogInformation($"Trying to download key from: {url}");
                    var keyResp = await client.GetAsync(url);
                    if (keyResp.IsSuccessStatusCode)
                    {
                        var keyDoc = JsonDocument.Parse(await keyResp.Content.ReadAsStringAsync());
                        if (keyDoc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            var lastKey = keyDoc.RootElement.EnumerateArray().LastOrDefault();
                            if (lastKey.ValueKind != JsonValueKind.Undefined)
                            {
                                if(lastKey.TryGetProperty("publicKey", out var pk)) pemKey = pk.GetString()!;
                                else if(lastKey.TryGetProperty("certificate", out var cert)) pemKey = cert.GetString()!;
                                if (!string.IsNullOrEmpty(pemKey)) break;
                            }
                        }
                    }
                }
                catch (Exception ex) { _logger.LogWarning($"Error fetching key: {ex.Message}"); }
            }

            if (string.IsNullOrEmpty(pemKey)) throw new Exception("Could not retrieve Public Key from API.");

            _logger.LogInformation($"Key found (start): {pemKey.Substring(0, Math.Min(30, pemKey.Length))}...");

            // --- ШАГ 3: Шифрование ---
            var encrypted = EncryptRsaOaepSha256($"{authToken}|{time}", pemKey);

            // --- ШАГ 4: Init Token ---
            var initUrl = $"{baseUrl}/auth/ksef-token";
            var authBody = new {
                challenge = challenge,
                contextIdentifier = new { type = idType, value = nip }, // Тут тоже idType
                encryptedToken = encrypted
            };            
            var initResp = await client.PostAsync(initUrl, 
                new StringContent(JsonSerializer.Serialize(authBody), Encoding.UTF8, "application/json"));
            
            var initContent = await initResp.Content.ReadAsStringAsync();
            if (!initResp.IsSuccessStatusCode) 
                throw new Exception($"Init Session Failed: {initContent}");

            var initDoc = JsonDocument.Parse(initContent);
            var refNum = initDoc.RootElement.GetProperty("referenceNumber").GetString();
            
            string tempToken = null!;
            // Ищем токен в разных местах (v2 может вернуть authenticationToken объект или сразу token)
            if (initDoc.RootElement.TryGetProperty("authenticationToken", out var authTokenElem))
            {
                tempToken = authTokenElem.GetProperty("token").GetString()!;
            }
            else if (initDoc.RootElement.TryGetProperty("token", out var tokenElem))
            {
                tempToken = tokenElem.GetString()!;
            }
            
            if(string.IsNullOrEmpty(tempToken))
                 throw new Exception("Could not extract Session Token from response.");

            // --- В API v2 ПОЛУЧЕННЫЙ ТОКЕН СРАЗУ АКТИВЕН. ШАГ 5 НЕ НУЖЕН. ---
            // (Проверка статуса удалена, так как endpoint /auth/status не существует)

            return (tempToken!, refNum!, DateTimeOffset.UtcNow.AddHours(23));
        }

        private string EncryptRsaOaepSha256(string data, string pemKey)
        {
            using var rsa = RSA.Create();
            var keyClean = pemKey
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "")
                .Replace("-----BEGIN CERTIFICATE-----", "")
                .Replace("-----END CERTIFICATE-----", "")
                .Replace("\\n", "").Replace("\n", "").Replace("\r", "").Trim();

            byte[] keyBytes = Convert.FromBase64String(keyClean);

            try 
            {
                rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
            }
            catch 
            {
                try 
                {
                    using var cert = X509CertificateLoader.LoadCertificate(keyBytes);
                    var certRsa = cert.GetRSAPublicKey();
                    if (certRsa == null) throw new Exception("Certificate has no RSA key");
                    var dataBytes2 = Encoding.UTF8.GetBytes(data);
                    return Convert.ToBase64String(certRsa.Encrypt(dataBytes2, RSAEncryptionPadding.OaepSHA256));
                }
                catch (Exception ex)
                {
                     throw new Exception($"Failed to parse Public Key. Error: {ex.Message}");
                }
            }

            var dataBytes = Encoding.UTF8.GetBytes(data);
            return Convert.ToBase64String(rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256));
        }
    }
}