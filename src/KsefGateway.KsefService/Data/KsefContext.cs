// \src\KsefGateway.KsefService\Data\KsefContext.cs
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Data.Entities;

namespace KsefGateway.KsefService.Data
{
    public class KsefContext : DbContext
    {
        public KsefContext(DbContextOptions<KsefContext> options) : base(options)
        {
        }

        // Таблица для хранения токенов (Чтобы не логиниться каждый раз)
        public DbSet<KsefSession> Sessions { get; set; }

        // Таблица-буфер для фактур (Очередь отправки)
        public DbSet<OutboundInvoice> OutboundInvoices { get; set; }

        // НОВАЯ ТАБЛИЦА
        public DbSet<AppSetting> Settings { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Дополнительные настройки можно писать здесь,
            // но мы уже использовали атрибуты [Key] и [Index] в классах сущностей.
        }
    }
}