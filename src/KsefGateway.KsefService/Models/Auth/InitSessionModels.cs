// src\KsefGateway.KsefService\Models\Auth\InitSessionModels.cs
// src\KsefGateway.KsefService\Models\Auth\InitSessionModels.cs
using System.Text.Json.Serialization;

namespace KsefGateway.KsefService.Models.Auth
{
    // === 1. МОДЕЛИ ДЛЯ АВТОРИЗАЦИИ ТОКЕНОМ (ТО, ЧТО У НАС РАБОТАЕТ) ===

    public class TokenAuthRequest
    {
        [JsonPropertyName("challenge")]
        public string Challenge { get; set; } = string.Empty;

        [JsonPropertyName("contextIdentifier")]
        public ContextIdentifier ContextIdentifier { get; set; } = new();

        [JsonPropertyName("encryptedToken")]
        public string EncryptedToken { get; set; } = string.Empty;
    }

    public class ContextIdentifier
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "Nip";

        [JsonPropertyName("value")] // В v2 это "value", а не "identifier"
        public string Value { get; set; } = string.Empty;
    }


    // === 2. МОДЕЛИ ДЛЯ ИНТЕРАКТИВНОЙ СЕССИИ (БУДУЩЕЕ/AES) ===

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
        public string EncryptedSymmetricKey { get; set; } = string.Empty; // Исправлено: инициализация

        [JsonPropertyName("initializationVector")]
        public string InitializationVector { get; set; } = string.Empty; // Исправлено: инициализация
    }
    
    // === 3. ОТВЕТЫ СЕРВЕРА (ОБЩИЕ) ===

    public class InitSessionResponse
    {
        [JsonPropertyName("referenceNumber")]
        public string ReferenceNumber { get; set; } = string.Empty; // Исправлено

        [JsonPropertyName("sessionToken")]
        public SessionTokenResponse SessionToken { get; set; } = new(); // Исправлено
    }

    public class SessionTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty; // Исправлено
        
        [JsonPropertyName("context")]
        public SessionContext Context { get; set; } = new(); // Исправлено
    }
    
    public class SessionContext
    {
        [JsonPropertyName("contextIdentifier")]
        public ContextIdentifier ContextIdentifier { get; set; } = new();
        
        // Другие поля контекста, если понадобятся...
    }
}