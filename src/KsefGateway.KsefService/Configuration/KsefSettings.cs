//  KsefGateway\src\KsefGateway.KsefService\Configuration\KsefSettings.cs
namespace KsefGateway.KsefService.Configuration;

public class KsefSettings
{
    // Окружение (Test / Demo / Prod)
    public string Environment { get; set; } = string.Empty;

    // Базовый URL (https://api-test.ksef.mf.gov.pl/v2)
    public string BaseUrl { get; set; } = string.Empty;

    // Новая секция для путей к методам
    public KsefEndpoints Endpoints { get; set; } = new KsefEndpoints();

    // Токен (в JSON он называется ApiToken)
    public string ApiToken { get; set; } = string.Empty;
}

// Класс для хранения путей (Endpoints)
public class KsefEndpoints
{
    public string Challenge { get; set; } = "/auth/challenge";
    public string Token { get; set; } = "/auth/ksef-token";
    public string PublicKey { get; set; } = "/security/public-key-certificates";
    public string Status { get; set; } = "/common/status";
}