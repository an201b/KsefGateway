// src/KsefGateway.KsefService/Program.cs
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services; // Убедитесь, что этот using есть
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Components;

var builder = WebApplication.CreateBuilder(args);

// === 1. РЕГИСТРАЦИЯ LogService (САМОЕ ВАЖНОЕ) ===
builder.Services.AddSingleton<LogService>(); // <--- ВОТ ЭТОГО НЕ ХВАТАЛО!

// === 2. НАСТРОЙКА ЛОГГЕРА ===
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Регистрируем провайдер, который будет пересылать логи в LogService
builder.Logging.Services.AddSingleton<ILoggerProvider, UiLoggerProvider>();

// --- 3. ОСТАЛЬНЫЕ СЕРВИСЫ ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.Configure<KsefSettings>(builder.Configuration.GetSection("Ksef"));

// База данных SQLite
var connectionString = "Data Source=ksef_gateway.db";
builder.Services.AddDbContext<KsefContext>(options => options.UseSqlite(connectionString));

// Ваши сервисы
builder.Services.AddScoped<KsefAuthService>(); 
builder.Services.AddHttpClient(); 
builder.Services.AddMemoryCache(); 
builder.Services.AddScoped<AppSettingsService>(); // Лучше Scoped для работы с БД

// Воркер
builder.Services.AddHostedService<KsefWorker>();

// UI (Blazor)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(); 

var app = builder.Build();

// --- 4. МИГРАЦИЯ БД ---
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
    try 
    {
        db.Database.Migrate();
        // WAL режим полезен для SQLite, чтобы не было блокировок
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        Console.WriteLine("--> Database migrated successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> !!! Database migration failed: {ex.Message}");
    }
}

// --- 5. PIPELINE ---
app.UseStaticFiles();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); 
}

app.UseAuthorization();
app.MapControllers(); 

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();