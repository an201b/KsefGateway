//  KsefGateway\src\KsefGateway.KsefService\Configuration\KsefSettings.cs
namespace KsefGateway.KsefService.Configuration;

public class KsefSettings
{
    public string Environment { get; set; } = "Demo";
    public string BaseUrl { get; set; } = string.Empty;
    public string PublicKeyUrl { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
}