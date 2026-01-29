using KsefGateway.KsefService.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace KsefGateway.KsefService.Data;

public class KsefContext : DbContext
{
    public KsefContext(DbContextOptions<KsefContext> options) : base(options)
    {
    }

    // Наша таблица счетов
    public DbSet<InvoiceRequest> Invoices { get; set; }
}