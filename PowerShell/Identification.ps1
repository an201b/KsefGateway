$path = "C:\Projects\KsefGateway\src\KsefGateway.KsefService\Services\KsefAuthService.cs"

$code = @"
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;

namespace KsefGateway.KsefService.Services
{
    public class KsefAuthService
    {
        private readonly KsefContext _context;
        private readonly AppSettingsService _settingsService;
        private readonly ILogger<KsefAuthService> _logger;

        public KsefAuthService(
            KsefContext context,
            AppSettingsService settingsService,
            ILogger<KsefAuthService> logger)
        {
            _context = context;
            _settingsService = settingsService;
            _logger = logger;
        }

        public async Task<string> GetAccessTokenAsync()
        {
            var nip = await _settingsService.GetValueAsync("Ksef:Nip");
            if (string.IsNullOrEmpty(nip)) nip = "5423240211"; // Fallback NIP

            var session = await _context.Sessions.FirstOrDefaultAsync(s => s.Nip == nip);
            if (session != null && session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                return session.AccessToken;
            }

            _logger.LogInformation("Session expired or missing. Performing Full Login (API v2)...");
            var (newToken, expires) = await PerformFullLoginV2Async(nip);

            if (session == null)
            {
                session = new KsefSession { Nip = nip };
                _context.Sessions.Add(session);
            }

            session.AccessToken = newToken;
            session.AccessTokenExpiresAt = expires;
            
            await _context.SaveChangesAsync();
            return newToken;
        }

        private async Task<(string AccessToken, DateTimeOffset ExpiresAt)> PerformFullLoginV2Async(string nip)
        {
            // === 1. ЧИТАЕМ НАСТРОЙКИ С ЗАЩИТОЙ ОТ ПУСТОТЫ ===
            var baseUrl = await _settingsService.GetValueAsync("Ksef:BaseUrl");
            if (string.IsNullOrWhiteSpace(baseUrl)) 
            {
                baseUrl = "https://api-test.ksef.mf.gov.pl/v2"; // ЗНАЧЕНИЕ ПО УМОЛЧАНИЮ
                _logger.LogWarning($"BaseUrl not found in DB. Using default: {baseUrl}");
            }
            baseUrl = baseUrl.TrimEnd('/');

            var publicKeyUrl = await _settingsService.GetValueAsync("Ksef:PublicKeyUrl");
            if (string.IsNullOrWhiteSpace(publicKeyUrl))
            {
                publicKeyUrl = "https://api-test.ksef.mf.gov.pl/v2/security/public-key-certificates";
            }

            var idType = "onip"; // Жестко onip для компаний
            
            var authToken = await _settingsService.GetValueAsync("Ksef:AuthToken");
            if (string.IsNullOrWhiteSpace(authToken))
            {
                // Если токена нет, пробуем хардкод (ваш токен)
                authToken = "20260127-EC-2B84314000-F8E8C1CAF9-7D|nip-5423240211|e2ca6da648d44d16aa22789a492dea7cc4af600e8a094aaba19f9a4db28d80f3";
            }
            authToken = authToken.Trim();

            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Add("User-Agent", "KsefGateway/1.0 (IntegrationTest)");

            // --- KROK 1: Challenge ---
            var challengeUrl = $"{baseUrl}/auth/challenge";
            
            // Correct JSON Structure (identifier + onip)
            var challengeBody = new 
            { 
                contextIdentifier = new { type = idType, identifier = nip } 
            };

            _logger.LogInformation($"POST {challengeUrl} (Type: {idType})");
            
            var challengeResp = await client.PostAsync(challengeUrl, 
                new StringContent(JsonSerializer.Serialize(challengeBody), Encoding.UTF8, "application/json"));
            
            var challengeContent = await challengeResp.Content.ReadAsStringAsync();
            if (!challengeResp.IsSuccessStatusCode)
                 throw new Exception($"Auth Init Failed: {challengeContent}");

            using var challengeDoc = JsonDocument.Parse(challengeContent);
            var timestampStr = challengeDoc.RootElement.GetProperty("timestamp").GetString(); 
            var challenge = challengeDoc.RootElement.GetProperty("challenge").GetString();
            var time = DateTimeOffset.Parse(timestampStr!).ToUnixTimeMilliseconds();

            // --- KROK 2: Public Key ---
            string rawKey = "";
            try 
            {
                _logger.LogInformation($"Downloading key from: {publicKeyUrl}");
                var keyResp = await client.GetAsync(publicKeyUrl);
                var keyContent = await keyResp.Content.ReadAsStringAsync();
                
                try 
                {
                    using var keyDoc = JsonDocument.Parse(keyContent);
                    JsonElement root = keyDoc.RootElement;
                    JsonElement keysArray = default;

                    if (root.ValueKind == JsonValueKind.Array) keysArray = root;
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("publicKeyCertificate", out var pkArr)) keysArray = pkArr;
                        else if (root.TryGetProperty("publicKey", out var pkArr2)) keysArray = pkArr2;
                    }

                    if (keysArray.ValueKind == JsonValueKind.Array)
                    {
                        var lastKeyObj = keysArray.EnumerateArray().LastOrDefault();
                        if (lastKeyObj.ValueKind != JsonValueKind.Undefined)
                        {
                            if(lastKeyObj.TryGetProperty("publicKey", out var pk)) rawKey = pk.GetString()!;
                            else if(lastKeyObj.TryGetProperty("certificate", out var cert)) rawKey = cert.GetString()!;
                        }
                    }
                }
                catch 
                {
                    rawKey = keyContent;
                }
                
                if (string.IsNullOrWhiteSpace(rawKey)) rawKey = keyContent;
            }
            catch (Exception ex) { throw new Exception($"Failed to download Public Key. Error: {ex.Message}"); }

            if (string.IsNullOrWhiteSpace(rawKey)) throw new Exception("Public Key content is empty.");

            // --- KROK 3: Encrypt ---
            var encryptedToken = EncryptRsaOaepSha256($"{authToken}|{time}", rawKey);

            // --- KROK 4: Token ---
            var initUrl = $"{baseUrl}/auth/token/challenge"; 
            
            var authBody = new {
                challenge = challenge,
                contextIdentifier = new { type = idType, identifier = nip },
                token = encryptedToken 
            };            
            
            var initResp = await client.PostAsync(initUrl, 
                new StringContent(JsonSerializer.Serialize(authBody), Encoding.UTF8, "application/json"));
            
            var initContent = await initResp.Content.ReadAsStringAsync();
            
            // Fallback for some endpoints
            if (!initResp.IsSuccessStatusCode && initResp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                initUrl = $"{baseUrl}/auth/ksef-token";
                initResp = await client.PostAsync(initUrl, new StringContent(JsonSerializer.Serialize(authBody), Encoding.UTF8, "application/json"));
                initContent = await initResp.Content.ReadAsStringAsync();
            }

            if (!initResp.IsSuccessStatusCode)
                throw new Exception($"Token Auth Failed: {initContent}");

            using var initDoc = JsonDocument.Parse(initContent);
            
            string sessionToken = "";
            if (initDoc.RootElement.TryGetProperty("authenticationToken", out var authTokenElem))
                 sessionToken = authTokenElem.GetProperty("token").GetString()!;
            else if (initDoc.RootElement.TryGetProperty("token", out var tElem))
                 sessionToken = tElem.GetString()!;

            if (string.IsNullOrEmpty(sessionToken)) throw new Exception("Token not found in response");

            return (sessionToken, DateTimeOffset.UtcNow.AddMinutes(90));
        }

        private string EncryptRsaOaepSha256(string data, string pemKey)
        {
            var keyClean = pemKey;
            keyClean = keyClean
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "")
                .Replace("-----BEGIN CERTIFICATE-----", "")
                .Replace("-----END CERTIFICATE-----", "")
                .Replace("\\n", ""); 
            keyClean = Regex.Replace(keyClean, @"\s+", "");

            try
            {
                var keyBytes = Convert.FromBase64String(keyClean);
                using var rsa = RSA.Create();
                try { rsa.ImportSubjectPublicKeyInfo(keyBytes, out _); }
                catch {
                    using var cert = X509CertificateLoader.LoadCertificate(keyBytes);
                    using var certRsa = cert.GetRSAPublicKey();
                    if (certRsa == null) throw new Exception("Certificate has no RSA key");
                    return Convert.ToBase64String(certRsa.Encrypt(Encoding.UTF8.GetBytes(data), RSAEncryptionPadding.OaepSHA256));
                }
                return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(data), RSAEncryptionPadding.OaepSHA256));
            }
            catch (FormatException)
            {
                var preview = keyClean.Length > 20 ? keyClean.Substring(0, 20) + "..." : keyClean;
                throw new Exception($"Invalid Base64 Key. Cleaned start: '{preview}'. Length: {keyClean.Length}");
            }
        }

        public async Task<string> SendInvoiceDirectAsync(string xmlContent)
        {
            _logger.LogInformation(">>> Sending Invoice to KSeF v2 (Test)...");

            var baseUrl = (await _settingsService.GetValueAsync("Ksef:BaseUrl"));
             if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://api-test.ksef.mf.gov.pl/v2";
            baseUrl = baseUrl.TrimEnd('/');

            var token = await GetAccessTokenAsync(); 

            var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);
            var fileSize = xmlBytes.Length;

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(xmlBytes);
            var hashBase64 = Convert.ToBase64String(hashBytes);

            var requestObj = new
            {
                invoiceHash = new
                {
                    fileSize = fileSize,
                    hashSHA = new { algorithm = "SHA-256", encoding = "Base64", value = hashBase64 }
                },
                invoicePayload = new
                {
                    type = "plain",
                    invoiceBody = xmlContent
                }
            };
            
            var jsonString = JsonSerializer.Serialize(requestObj);
            
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            using var client = new HttpClient(handler);
            var endpoint = $"{baseUrl}/online/Invoice/Send";

            client.DefaultRequestHeaders.Add("SessionToken", token);
            client.DefaultRequestHeaders.Add("User-Agent", "KsefGateway/1.0 (IntegrationTest)");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var httpContent = new StringContent(jsonString, Encoding.UTF8, "application/json");
            httpContent.Headers.ContentLength = Encoding.UTF8.GetByteCount(jsonString);

            _logger.LogInformation($"PUT {endpoint} | Size: {fileSize} bytes");

            var response = await client.PutAsync(endpoint, httpContent);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"❌ KSeF v2 Error ({response.StatusCode}): {responseContent}");
                throw new Exception($"KSeF v2 Error ({response.StatusCode}): {responseContent}");
            }

            using var doc = JsonDocument.Parse(responseContent);
            var refNum = doc.RootElement.GetProperty("elementReferenceNumber").GetString();
            var code = doc.RootElement.GetProperty("processingCode").GetString();

            _logger.LogInformation($"✅ SUCCESS! Ref: {refNum}, Code: {code}");
            return refNum ?? "SENT";
        }
    }
}
"@

Set-Content -Path $path -Value $code -Encoding UTF8
Write-Host "✅ Код защищен от пустого URL!" -ForegroundColor Green