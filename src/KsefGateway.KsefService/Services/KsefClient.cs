using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Models;
using Microsoft.Extensions.Options;

namespace KsefGateway.KsefService.Services;

public class KsefClient
{
    private readonly HttpClient _httpClient;
    private readonly KsefSettings _settings;

    public KsefClient(HttpClient httpClient, IOptions<KsefSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        // Обязательные заголовки
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Получение Authorisation Challenge (KSeF v2)
    /// </summary>
    public async Task<KsefChallengeResponse> GetAuthorisationChallengeAsync(
        string nip,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nip))
            throw new ArgumentException("NIP не может быть пустым", nameof(nip));

        var url =
            $"{_settings.BaseUrl}/online/Session/AuthorisationChallenge";

        // ❗ Строго по спецификации v2
        var requestBody = new
        {
            contextIdentifierType = "nip",
            contextIdentifier = nip
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response =
            await _httpClient.PostAsync(url, content, cancellationToken);

        var responseBody =
            await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"KSeF AuthorisationChallenge error ({(int)response.StatusCode}): {responseBody}");
        }

        using var json = JsonDocument.Parse(responseBody);
        var root = json.RootElement;

        // KSeF v2 всегда возвращает timestamp строкой ISO-8601
        var timestampIso = root.GetProperty("timestamp").GetString();
        if (timestampIso is null)
            throw new InvalidOperationException("Ответ KSeF не содержит timestamp");

        var timestampMs = DateTimeOffset
            .Parse(timestampIso)
            .ToUnixTimeMilliseconds();

        var challenge = root.GetProperty("challenge").GetString();
        if (string.IsNullOrWhiteSpace(challenge))
            throw new InvalidOperationException("Ответ KSeF не содержит challenge");

        return new KsefChallengeResponse
        {
            Timestamp = timestampMs,
            Challenge = challenge
        };
    }
}
