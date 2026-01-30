// src\KsefGateway.KsefService\Controllers\InvoiceController.cs
// DTO для приема данных от 1С
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;

namespace KsefGateway.KsefService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class InvoiceController : ControllerBase
    {
        private readonly KsefContext _context;

        public InvoiceController(KsefContext context)
        {
            _context = context;
        }

        // === 1. ПРИЕМ ФАКТУРЫ ОТ 1С (Буферизация) ===
        // POST /api/Invoice/send
        [HttpPost("send")]
        public async Task<IActionResult> EnqueueInvoice([FromBody] InvoiceDto request)
        {
            // Валидация входных данных
            if (string.IsNullOrEmpty(request.InvoiceNumber) || string.IsNullOrEmpty(request.XmlBody))
                return BadRequest("InvoiceNumber and XmlBody are required.");

            // 1. Идемпотентность: проверяем, не прислали ли нам это уже
            var existing = await _context.OutboundInvoices
                .FirstOrDefaultAsync(i => i.InvoiceNumber == request.InvoiceNumber);

            if (existing != null)
            {
                return Ok(new 
                { 
                    Message = "Invoice already exists", 
                    Id = existing.Id, 
                    Status = existing.Status.ToString(),
                    KsefReference = existing.KsefReferenceNumber
                });
            }

            // 2. Создаем запись в буфере
            var invoice = new OutboundInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = request.InvoiceNumber,
                XmlContent = request.XmlBody,
                Status = InvoiceStatus.New, // <--- Воркер ищет именно этот статус
                CreatedAt = DateTimeOffset.UtcNow
            };

            // 3. Сохраняем в SQLite
            _context.OutboundInvoices.Add(invoice);
            await _context.SaveChangesAsync();

            // 4. Возвращаем 202 Accepted
            return Accepted(new 
            { 
                Id = invoice.Id, 
                Status = "Buffered", 
                Message = "Invoice queued for sending to KSeF." 
            });
        }

        // === 2. ПРОВЕРКА СТАТУСА ===
        // GET /api/Invoice/status/FV-123
        [HttpGet("status/{invoiceNumber}")]
        public async Task<IActionResult> GetStatus(string invoiceNumber)
        {
            var invoice = await _context.OutboundInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

            if (invoice == null)
                return NotFound("Invoice not found in local buffer.");

            return Ok(new 
            { 
                InvoiceNumber = invoice.InvoiceNumber,
                Status = invoice.Status.ToString(),
                KsefRef = invoice.KsefReferenceNumber,
                ErrorMessage = invoice.ErrorMessage,
                SentAt = invoice.SentAt,
                HasUpo = !string.IsNullOrEmpty(invoice.UpoXmlContent)
            });
        }

        // === 3. ПОЛУЧЕНИЕ UPO (Квитанции) ===
        // GET /api/Invoice/upo/FV-123
        [HttpGet("upo/{invoiceNumber}")]
        public async Task<IActionResult> GetUpo(string invoiceNumber)
        {
            var invoice = await _context.OutboundInvoices
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.InvoiceNumber == invoiceNumber);

            if (invoice == null) return NotFound();
            
            if (string.IsNullOrEmpty(invoice.UpoXmlContent))
                return BadRequest(new { Error = "UPO not yet available", Status = invoice.Status.ToString() });

            return Content(invoice.UpoXmlContent, "application/xml");
        }
    }

    // DTO
    public class InvoiceDto
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public string XmlBody { get; set; } = string.Empty;
    }
}