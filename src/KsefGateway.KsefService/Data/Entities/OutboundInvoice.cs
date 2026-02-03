// Data/Entities/OutboundInvoice.cs
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KsefGateway.KsefService.Data.Entities
{
    [Index(nameof(InvoiceNumber))] 
    [Index(nameof(Status))]        
    public class OutboundInvoice
    {
        [Key]
        public Guid Id { get; set; } 
        
        [Required]
        [MaxLength(50)]
        public string InvoiceNumber { get; set; } = string.Empty; 
        
        public DateTime? ProcessedAt { get; set; }  // Время последней попытки обработки
        
        public string? KsefReferenceNumber { get; set; } 
        
        // Используем Enum. EF Core сохранит его как int.
        public InvoiceStatus Status { get; set; } = InvoiceStatus.New;
        
        public string XmlContent { get; set; } = string.Empty; 
        
        public string? UpoXmlContent { get; set; } 
        
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? SentAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum InvoiceStatus
    {
        New = 0,            
        Processing = 1,     
        SentToKsef = 2,     // Фактура ушла в KSeF, получен ReferenceNumber
        Success = 3,        // (На будущее) Получено UPO
        Rejected = 4        // Ошибка отправки или валидации
    }
}