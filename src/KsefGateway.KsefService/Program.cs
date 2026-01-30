// src/KsefGateway.KsefService/Program.cs
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Data; // Оставляем один раз
using KsefGateway.KsefService.Services;
using Microsoft.EntityFrameworkCore;
using KSeF.Client.DI;

// === USING (Для старой библиотеки, если она еще нужна) ===
using KSeF.Client.Core.Interfaces;          
using KSeF.Client.Core.Interfaces.Services; 
using KSeF.Client.Core.Interfaces.Clients; 
using KSeF.Client.Api.Services;             
using KSeF.Client.Api.Services.Internal;
using KSeF.Client.Clients;

var builder = WebApplication.CreateBuilder(args);

// --- СТАНДАРТНЫЕ СЕРВИСЫ ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.Configure<KsefSettings>(builder.Configuration.GetSection("Ksef"));

// --- БАЗА ДАННЫХ (SQLite) ---
// УДАЛЕНО: UseNpgsql (Postgres нам больше не нужен)
// ДОБАВЛЕНО: UseSqlite (Файл создастся в папке с приложением)
var connectionString = "Data Source=ksef_gateway.db";
builder.Services.AddDbContext<KsefContext>(options =>
    options.UseSqlite(connectionString));

// --- 1. БИБЛИОТЕКА KSEF (Legacy) ---
builder.Services.AddKSeFClient(options =>
{
    options.BaseUrl = builder.Configuration["Ksef:BaseUrl"] ?? "https://ksef-demo.mf.gov.pl/api";
});

// --- 2. РУЧНАЯ РЕГИСТРАЦИЯ (Если требуется для старого кода) ---
builder.Services.AddScoped<ICryptographyClient, CryptographyClient>();
builder.Services.AddScoped<ICertificateFetcher, DefaultCertificateFetcher>();
builder.Services.AddScoped<ICryptographyService, CryptographyService>();
builder.Services.AddScoped<IAuthCoordinator, AuthCoordinator>();

// --- 3. ВАШИ СЕРВИСЫ ---
// Здесь будет жить новый AuthService
builder.Services.AddScoped<KsefAuthService>(); // <-- Новый сервис
// builder.Services.AddScoped<KsefIntegrationService>(); // Старый пока можно оставить или удалить
builder.Services.AddHttpClient(); // Обязательно для нашего SystemController
builder.Services.AddMemoryCache(); // Для кэширования настроек
builder.Services.AddSingleton<AppSettingsService>(); // Singleton, чтобы кэш жил долго
// 3. ВОРКЕР (Фоновая задача) - ДОБАВИТЬ ЭТУ СТРОКУ
builder.Services.AddHostedService<KsefWorker>();

var app = builder.Build();

// --- АВТО-МИГРАЦИЯ (Zero-Config) ---
// При каждом запуске проверяем, есть ли файл .db, и создаем таблицы
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
    try 
    {
        db.Database.Migrate();
        // МАГИЯ СКОРОСТИ: Включаем WAL режим
        // Это позволяет читать и писать одновременно!
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        Console.WriteLine("--> Database migrated successfully (Zero-Config).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> !!! Database migration failed: {ex.Message}");
    }
}

// --- PIPELINE ---
app.UseSwagger();
app.UseSwaggerUI(c => 
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "KSeF Gateway API v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthorization();
app.MapControllers();

app.Run();