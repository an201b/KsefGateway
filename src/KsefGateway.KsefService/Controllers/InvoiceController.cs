using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Для ToListAsync

namespace KsefGateway.KsefService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoiceController : ControllerBase
{
    private readonly KsefContext _context;

    // Внедряем базу данных через конструктор
    public InvoiceController(KsefContext context)
    {
        _context = context;
    }

    // 1. Метод для приема счета из 1С
    // POST: api/invoice/send
    [HttpPost("send")]
    public async Task<IActionResult> SendInvoice([FromBody] InvoiceDto request)
    {
        // Простая валидация
        if (string.IsNullOrEmpty(request.XmlContent))
            return BadRequest("XML не может быть пустым");

        // Создаем запись для базы
        var invoice = new InvoiceRequest
        {
            SourceNumber = request.SourceNumber,
            IssuerNip = request.IssuerNip,
            CustomerNip = request.CustomerNip,
            XmlContent = request.XmlContent,
            GrossAmount = request.GrossAmount,
            Status = "New", // Сразу ставим статус "Новый"
            CreatedAt = DateTime.UtcNow
        };

        // Сохраняем в базу
        _context.Invoices.Add(invoice);
        await _context.SaveChangesAsync();

        return Ok(new { 
            Id = invoice.Id, 
            Message = "Счет сохранен в шлюзе и ждет отправки в KSeF",
            Status = invoice.Status 
        });
    }

    // 2. Метод для просмотра списка счетов (чтобы проверить, что сохранилось)
    // GET: api/invoice/list
    [HttpGet("list")]
    public async Task<IActionResult> GetList()
    {
        var list = await _context.Invoices
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .ToListAsync();
            
        return Ok(list);
    }
}

// DTO - объект для передачи данных (что мы ждем от 1С)
public class InvoiceDto
{
    public string SourceNumber { get; set; } = string.Empty;
    public string IssuerNip { get; set; } = string.Empty;
    public string CustomerNip { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public string XmlContent { get; set; } = string.Empty;
}