// src\KsefGateway.KsefService\Models\Auth\InitSessionModels.cs
using System.Text.Json.Serialization;

namespace KsefGateway.KsefService.Models.Auth
{
    public class InitSessionRequest
    {
        [JsonPropertyName("context")]
        public SessionContext Context { get; set; } = new();
    }
    
    public class InteractiveSessionRequest
    {
        [JsonPropertyName("formCode")]
        public FormCode FormCode { get; set; } = new();

        [JsonPropertyName("encryption")]
        public EncryptionConfig Encryption { get; set; } = new();
    }

    public class FormCode
    {
        [JsonPropertyName("systemCode")]
        public string SystemCode { get; set; } = "FA (2)";

        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "1-0E";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "FA";
    }

    public class EncryptionConfig
    {
        [JsonPropertyName("encryptedSymmetricKey")]
        public string EncryptedSymmetricKey { get; set; } = string.Empty;

        [JsonPropertyName("initializationVector")]
        public string InitializationVector { get; set; } = string.Empty;
    }
    
    public class InitSessionResponse
    {
        [JsonPropertyName("referenceNumber")]
        public string? ReferenceNumber { get; set; } // Может быть null

        [JsonPropertyName("sessionToken")]
        public SessionTokenResponse? SessionToken { get; set; }
    }

    public class SessionTokenResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
        
        [JsonPropertyName("context")]
        public object? Context { get; set; }
    }
    
    public class SessionContext
    {
    }
}