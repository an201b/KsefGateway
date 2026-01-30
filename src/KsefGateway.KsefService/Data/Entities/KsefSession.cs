// Data/Entities/KsefSession.cs
using System.ComponentModel.DataAnnotations;

namespace KsefGateway.KsefService.Data.Entities
{
    public class KsefSession
    {
        [Key]
        public string Nip { get; set; } = string.Empty; // ID сессии = NIP организации
        
        public string Environment { get; set; } = "Demo"; 
        
        public string AccessToken { get; set; } = string.Empty; // Токен для заголовка
        
        public string RefreshToken { get; set; } = string.Empty; // Токен для обновления
        
        public DateTimeOffset AccessTokenExpiresAt { get; set; } // Срок жизни
    }
}