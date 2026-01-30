//  src\KsefGateway.KsefService\Services\KsefAuthService.cs
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;
using System.Net.Http.Headers;

namespace KsefGateway.KsefService.Services
{
    /// <summary>
    /// Сервис отвечает за аутентификацию в KSeF.
    /// Реализует паттерн "Transparent Proxy" (Прозрачный прокси) с кэшированием сессии в SQLite.
    /// </summary>
    public class KsefAuthService
    {
        private readonly KsefContext _dbContext;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppSettingsService _settingsService; // Используем сервис настроек
        
        // Семафор для реализации блокировки (аналог asyncio.Lock).
        // Гарантирует, что только один поток выполняет вход, остальные ждут.
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public KsefAuthService(
            KsefContext dbContext,
            IHttpClientFactory httpClientFactory,
            AppSettingsService settingsService)
        {
            _dbContext = dbContext;
            _httpClientFactory = httpClientFactory;
            _settingsService = settingsService;
        }

        /// <summary>
        /// Возвращает действующий Access Token.
        /// Если токен есть в БД и жив -> возвращает мгновенно.
        /// Если токена нет -> делает полный цикл входа в KSeF и сохраняет результат.
        /// </summary>
        public async Task<string> GetAccessTokenAsync()
        {
            // 1. Получаем NIP из динамических настроек
            var nip = (await _settingsService.GetValueAsync("Ksef:Nip")).Trim();

            if (string.IsNullOrEmpty(nip))
                throw new Exception("NIP not configured. Please set 'Ksef:Nip' via Settings API.");

            // 2. Быстрая проверка в БД (без блокировки для скорости)
            var session = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Nip == nip);

            if (IsValid(session))
            {
                return session.AccessToken;
            }

            // 3. Если токена нет или он протух, входим в критическую секцию (Lock)
            await _lock.WaitAsync();
            try
            {
                // Double-check locking: проверяем еще раз, вдруг другой поток уже обновил токен
                session = await _dbContext.Sessions.FirstOrDefaultAsync(s => s.Nip == nip);
                if (IsValid(session))
                {
                    return session.AccessToken;
                }

                // 4. Выполняем ПОЛНЫЙ цикл входа (Challenge -> Init -> Wait -> Redeem)
                var newTokens = await PerformFullLoginAsync(nip);

                // 5. Сохраняем (Upsert) сессию в SQLite
                if (session == null)
                {
                    session = new KsefSession { Nip = nip };
                    _dbContext.Sessions.Add(session);
                }

                session.AccessToken = newTokens.AccessToken;
                session.RefreshToken = newTokens.RefreshToken;
                session.AccessTokenExpiresAt = newTokens.ExpiresAt;
                session.Environment = "Demo"; 

                await _dbContext.SaveChangesAsync();

                return session.AccessToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        private bool IsValid(KsefSession? session)
        {
            // Считаем токен валидным, если до его смерти осталось более 2 минут
            if (session == null) return false;
            return session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2);
        }

        // === ЛОГИКА ВЗАИМОДЕЙСТВИЯ С API KSeF ===
        private async Task<(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt)> PerformFullLoginAsync(string nip)
        {
            // Читаем URL и AuthToken из настроек
            var baseUrl = (await _settingsService.GetValueAsync("Ksef:BaseUrl")).TrimEnd('/');
            var authToken = (await _settingsService.GetValueAsync("Ksef:AuthToken")).Trim();

            if (string.IsNullOrEmpty(authToken))
                throw new Exception("AuthToken not configured. Set 'Ksef:AuthToken'.");

            var client = _httpClientFactory.CreateClient();

            // --- ШАГ 1: Challenge ---
            var challengeResp = await client.PostAsync($"{baseUrl}/auth/challenge", 
                new StringContent("{}", Encoding.UTF8, "application/json"));
            
            if (!challengeResp.IsSuccessStatusCode)
                 throw new Exception($"Challenge Error: {await challengeResp.Content.ReadAsStringAsync()}");

            var challengeDoc = JsonDocument.Parse(await challengeResp.Content.ReadAsStringAsync());
            var ts = challengeDoc.RootElement.GetProperty("timestampMs").GetInt64();
            var challenge = challengeDoc.RootElement.GetProperty("challenge").GetString();

            // --- ШАГ 2: Public Key ---
            var keyResp = await client.GetAsync($"{baseUrl}/security/public-key-certificates");
            var keyDoc = JsonDocument.Parse(await keyResp.Content.ReadAsStringAsync());
            string? pemKey = null;
            // Ищем сертификат в ответе
            if (keyDoc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in keyDoc.RootElement.EnumerateArray()) {
                    if (item.TryGetProperty("certificate", out var c)) { pemKey = c.GetString(); break; }
                }
            }
            
            if (string.IsNullOrEmpty(pemKey)) throw new Exception("Could not retrieve Public Key from KSeF.");

            // --- ШАГ 3: Шифрование (AuthToken + Timestamp) ---
            var encrypted = EncryptRsaOaepSha256($"{authToken}|{ts}", pemKey);

            // --- ШАГ 4: Инициализация сессии ---
            var authBody = new {
                challenge = challenge,
                contextIdentifier = new { type = "nip", value = nip },
                encryptedToken = encrypted
            };
            
            var initResp = await client.PostAsync($"{baseUrl}/auth/ksef-token", 
                new StringContent(JsonSerializer.Serialize(authBody), Encoding.UTF8, "application/json"));
            
            if (!initResp.IsSuccessStatusCode) 
                throw new Exception($"Init Session Failed: {await initResp.Content.ReadAsStringAsync()}");

            var initDoc = JsonDocument.Parse(await initResp.Content.ReadAsStringAsync());
            var refNum = initDoc.RootElement.GetProperty("referenceNumber").GetString();
            var tempToken = initDoc.RootElement.GetProperty("authenticationToken").GetProperty("token").GetString();

            // --- ШАГ 5: Цикл ожидания (Wait Loop) ---
            // KSeF проверяет подпись асинхронно. Нужно опрашивать статус.
            var statusUrl = $"{baseUrl}/auth/{refNum}";
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tempToken);
            
            bool isReady = false;
            // Ждем до 60 секунд (20 попыток * 3 сек)
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(3000);
                var statusResp = await client.GetAsync(statusUrl);
                if (statusResp.IsSuccessStatusCode)
                {
                    var code = JsonDocument.Parse(await statusResp.Content.ReadAsStringAsync())
                        .RootElement.GetProperty("status").GetProperty("code").GetInt32();
                    
                    // Код 200 = Успех, сессия готова
                    if (code == 200) { isReady = true; break; }
                }
                // Игнорируем 404/400 пока ждем, это нормально для процесса обработки
            }

            if (!isReady) throw new Exception("KSeF Login Timeout: Session verification took too long.");

            // --- ШАГ 6: Финальный обмен (Redeem) ---
            var redeemResp = await client.PostAsync($"{baseUrl}/auth/token/redeem", 
                new StringContent("{}", Encoding.UTF8, "application/json"));
            
            if (!redeemResp.IsSuccessStatusCode)
                 throw new Exception($"Redeem Failed: {await redeemResp.Content.ReadAsStringAsync()}");

            var redeemDoc = JsonDocument.Parse(await redeemResp.Content.ReadAsStringAsync());
            
            var accessToken = redeemDoc.RootElement.GetProperty("accessToken").GetProperty("token").GetString()!;
            var refreshToken = redeemDoc.RootElement.GetProperty("refreshToken").GetProperty("token").GetString()!;
            
            // Парсинг времени истечения
            var validUntilStr = redeemDoc.RootElement.GetProperty("accessToken").GetProperty("validUntil").GetString();
            var expiresAt = DateTimeOffset.Parse(validUntilStr!);

            return (accessToken, refreshToken, expiresAt);
        }

        // Вспомогательный метод шифрования RSA
        private string EncryptRsaOaepSha256(string data, string certContent)
        {
            var certBytes = Convert.FromBase64String(certContent);
            using var cert = X509CertificateLoader.LoadCertificate(certBytes);
            using var rsa = cert.GetRSAPublicKey() ?? throw new Exception("No RSA key found in certificate");
            
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var encryptedBytes = rsa.Encrypt(dataBytes, RSAEncryptionPadding.OaepSHA256);
            
            return Convert.ToBase64String(encryptedBytes);
        }
    }
}