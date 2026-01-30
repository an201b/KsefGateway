// src\KsefGateway.KsefService\Data\Entities\AppSetting.cs
using System.ComponentModel.DataAnnotations;

namespace KsefGateway.KsefService.Data.Entities
{
    public class AppSetting
    {
        [Key]
        public string Key { get; set; } = string.Empty; // Например: "Ksef:Nip"

        public string Value { get; set; } = string.Empty; // Например: "1234567890"

        public string? Description { get; set; } // Для админки (Swagger)
    }
}