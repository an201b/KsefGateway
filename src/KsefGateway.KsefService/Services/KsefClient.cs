// src\KsefGateway.KsefService\Services\KsefClient.cs
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Models;
using Microsoft.Extensions.Options;

namespace KsefGateway.KsefService.Services;

// Простая модель для результата входа
public class KsefSessionResult
{
    public string Token { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
}

public class KsefClient
{
    private readonly HttpClient _httpClient;
    private readonly KsefSettings _settings;
    private readonly ILogger<KsefClient> _logger;

    public KsefClient(HttpClient httpClient, IOptions<KsefSettings> settings, ILogger<KsefClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

#pragma warning disable SYSLIB0014
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#pragma warning restore SYSLIB0014

        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("KsefGateway/1.0");
    }

    // === 1. CHALLENGE ===
    public async Task<KsefChallengeResponse> GetChallengeAsync(string nip)
    {
        var url = $"{_settings.BaseUrl}{_settings.Endpoints.Challenge}";
        var body = new { contextIdentifier = new { type = "Nip", value = nip } };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        _logger.LogInformation($"[KsefClient] POST {url}");
        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Challenge Error ({response.StatusCode}): {err}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        long ts;
        if (root.TryGetProperty("timestampMs", out var tsEl)) ts = tsEl.GetInt64();
        else {
            var d = DateTime.Parse(root.GetProperty("timestamp").GetString()!).ToUniversalTime();
            ts = (long)(d - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        return new KsefChallengeResponse { Timestamp = ts, Challenge = root.GetProperty("challenge").GetString()! };
    }

    // === 2. PUBLIC KEY ===
    public async Task<string> GetPublicKeyAsync()
    {
        var url = $"{_settings.BaseUrl}{_settings.Endpoints.PublicKey}";
        var response = await _httpClient.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) 
            throw new Exception($"PublicKey Error ({response.StatusCode}): {content}");

        try 
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var firstItem = root[0];
                if (firstItem.ValueKind == JsonValueKind.Object)
                {
                    if (firstItem.TryGetProperty("certificate", out var certProp)) return certProp.GetString()!;
                    if (firstItem.TryGetProperty("publicKey", out var pkProp))
                    {
                        if (pkProp.ValueKind == JsonValueKind.Object && pkProp.TryGetProperty("publicKey", out var nested)) return nested.GetString()!;
                        return pkProp.GetString()!;
                    }
                }
            }
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("certificate", out var c)) return c.GetString()!;
                if (root.TryGetProperty("publicKeyCertificate", out var pc)) return pc.GetString()!;
                if (root.TryGetProperty("publicKey", out var pk)) return pk.ValueKind == JsonValueKind.Object ? pk.GetProperty("publicKey").GetString()! : pk.GetString()!;
            }
        }
        catch { _logger.LogWarning("Key parsing warning, using raw."); }

        return content; 
    }

    // === 3. INIT TOKEN (Возвращает объект!) ===
    public async Task<KsefSessionResult> InitTokenAsync(string nip, string challenge, string encryptedToken)
    {
        var url = $"{_settings.BaseUrl}{_settings.Endpoints.Token}";
        var body = new {
            challenge = challenge,
            contextIdentifier = new { type = "Nip", value = nip },
            encryptedToken = encryptedToken
        };
        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        
        _logger.LogInformation($"[KsefClient] POST {url}");
        var response = await _httpClient.PostAsync(url, content);
        var respJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode) throw new Exception($"InitToken Error ({response.StatusCode}): {respJson}");

        using var doc = JsonDocument.Parse(respJson);
        var root = doc.RootElement;
        
        var result = new KsefSessionResult();

        // 1. Достаем ReferenceNumber (ОБЯЗАТЕЛЬНО ДЛЯ ОТПРАВКИ)
        if (root.TryGetProperty("referenceNumber", out var refNum))
            result.ReferenceNumber = refNum.GetString()!;

        // 2. Достаем Token
        if (root.TryGetProperty("authenticationToken", out var authT))
             result.Token = authT.GetProperty("token").GetString()!;
        else if (root.TryGetProperty("sessionToken", out var st))
             result.Token = st.ValueKind == JsonValueKind.Object ? st.GetProperty("token").GetString()! : st.GetString()!;
        else if (root.TryGetProperty("token", out var t)) 
             result.Token = t.GetString()!;

        if (string.IsNullOrEmpty(result.Token)) throw new Exception("Token not found in response");

        return result;
    }

// === 4. SEND INVOICE (FIX: Authorization Header) ===
    public async Task<string> SendInvoiceAsync(string xmlContent, string sessionToken, string sessionReferenceNumber)
    {
        // 1. URL (Как нашли в OpenAPI)
        var url = $"{_settings.BaseUrl}/sessions/online/{sessionReferenceNumber}/invoices";
        
        // 2. Хеш
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);
        using var sha = SHA256.Create();
        var hash = Convert.ToBase64String(sha.ComputeHash(xmlBytes));
        
        // 3. Тело (Plain text, JSON serializer сам экранирует кавычки)
        var body = new {
            invoiceHash = new {
                fileSize = xmlBytes.Length,
                hashSHA = new { algorithm = "SHA-256", encoding = "Base64", value = hash }
            },
            invoicePayload = new {
                type = "plain",
                invoiceBody = xmlContent
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        
        // !!! ГЛАВНОЕ ИСПРАВЛЕНИЕ: Используем Bearer Token !!!
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        // Добавляем старый заголовок на всякий случай (для совместимости)
        request.Headers.TryAddWithoutValidation("SessionToken", sessionToken);
        
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogInformation($"[KsefClient] Sending Invoice...");
        _logger.LogInformation($"   URL: {url}");
        _logger.LogInformation($"   Auth: Bearer {sessionToken.Substring(0, 5)}...");

        var response = await _httpClient.SendAsync(request);
        var respString = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError($"[SendError] Status: {response.StatusCode}");
            _logger.LogError($"[SendError] Body: {respString}");
            throw new Exception($"SendInvoice Failed ({response.StatusCode}): {respString}");
        }

        using var doc = JsonDocument.Parse(respString);
        return doc.RootElement.GetProperty("elementReferenceNumber").GetString()!;
    }

    // === 5. ENCRYPT ===
    public string EncryptToken(string authToken, long timestamp, string publicKeyPem)
    {
        var msg = $"{authToken}|{timestamp}";
        var cleanKey = Regex.Replace(publicKeyPem
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\\n", "").Replace("\n", "").Replace("\r", ""), @"\s+", "");

        try
        {
            var keyBytes = Convert.FromBase64String(cleanKey);
            using var rsa = RSA.Create();
            try { rsa.ImportSubjectPublicKeyInfo(keyBytes, out _); }
            catch {
                 using var cert = X509CertificateLoader.LoadCertificate(keyBytes);
                 using var certRsa = cert.GetRSAPublicKey();
                 return Convert.ToBase64String(certRsa!.Encrypt(Encoding.UTF8.GetBytes(msg), RSAEncryptionPadding.OaepSHA256));
            }
            return Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes(msg), RSAEncryptionPadding.OaepSHA256));
        }
        catch { throw new Exception("Invalid Key"); }
    }
}