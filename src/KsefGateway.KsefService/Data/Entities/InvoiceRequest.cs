using System.ComponentModel.DataAnnotations;

namespace KsefGateway.KsefService.Data.Entities;

public class InvoiceRequest
{
    [Key]
    public int Id { get; set; }

    // Внутренний номер документа в 1С (например "FV/2026/01/15")
    [Required]
    [MaxLength(50)]
    public string SourceNumber { get; set; } = string.Empty;

    // NIP продавца (наш)
    [Required]
    [MaxLength(10)]
    public string IssuerNip { get; set; } = string.Empty;

    // NIP покупателя
    [MaxLength(10)]
    public string CustomerNip { get; set; } = string.Empty;

    // Итоговая сумма (для сверки)
    public decimal GrossAmount { get; set; }

    // Сам XML файл (будем хранить его текст, чтобы иметь архив)
    public string XmlContent { get; set; } = string.Empty;

    // Дата создания записи в нашей базе
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Статус обработки (New, Sending, Sent, Rejected)
    [MaxLength(20)]
    public string Status { get; set; } = "New";

    // KSeF ID (35 символов), который мы получим от налоговой
    [MaxLength(35)]
    public string? KsefReferenceNumber { get; set; }

    // Сообщение об ошибке (если KSeF отвергнет)
    public string? ErrorMessage { get; set; }
}