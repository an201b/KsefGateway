// src\KsefGateway.KsefService\Models\Auth\InitSessionModels.cs
using System.Text.Json.Serialization;

namespace KsefGateway.KsefService.Models.Auth
{
    // Главный объект запроса
    public class InitSessionRequest
    {
        [JsonPropertyName("context")]
        public SessionContext Context { get; set; } = new();

        // Мы не используем encryptedSymmetricKey в корне для InitToken, 
        // но для InitSession (шифрованной) структура может отличаться.
        // Однако, согласно документации KSeF v2 "InitSession", ключи часто идут 
        // либо в корне, либо внутри context.
        // Сделаем универсальную структуру, но опираясь на ваш пример:
        /*
           Ваш пример:
           {
              "formCode": { ... },
              "encryption": { ... }  <-- Выделим это
           }
        */
    }
    
    // Специальная модель ровно под ваш запрос (Otwarcie sesji interaktywnej)
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
        public string SystemCode { get; set; } = "FA (2)"; // Используем FA(2) пока что

        [JsonPropertyName("schemaVersion")]
        public string SchemaVersion { get; set; } = "1-0E";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "FA";
    }

    public class EncryptionConfig
    {
        [JsonPropertyName("encryptedSymmetricKey")]
        public string EncryptedSymmetricKey { get; set; }

        [JsonPropertyName("initializationVector")]
        public string InitializationVector { get; set; }
    }
    
    // Ответ сервера
    public class InitSessionResponse
    {
        [JsonPropertyName("referenceNumber")]
        public string ReferenceNumber { get; set; }

        [JsonPropertyName("sessionToken")]
        public SessionTokenResponse SessionToken { get; set; }
    }

    public class SessionTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }
        
        [JsonPropertyName("context")]
        public object Context { get; set; }
    }
    
    // Вспомогательный класс для контекста (если понадобится позже)
    public class SessionContext
    {
       // ... поля контекста (Identifier, Challenge и т.д.)
    }
}