namespace KsefGateway.KsefService.Models;

public class KsefChallengeResponse
{
    public long Timestamp { get; set; }
    public string Challenge { get; set; } = string.Empty;
}