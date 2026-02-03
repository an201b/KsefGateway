// src/KsefGateway.KsefService/Program.cs
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services;
using KsefGateway.KsefService.Components; // Для Blazor (App.razor)
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ===================================================
// 1. ЛОГИРОВАНИЕ (UI + CONSOLE)
// ===================================================
// Сервис для хранения логов в памяти (чтобы показывать в Blazor)
builder.Services.AddSingleton<LogService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Подключаем наш кастомный провайдер, который шлет логи в LogService
builder.Logging.Services.AddSingleton<ILoggerProvider, UiLoggerProvider>();

// ===================================================
// 2. КОНФИГУРАЦИЯ И БАЗА ДАННЫХ
// ===================================================
// !!! ВАЖНО: Имя секции должно совпадать с appsettings.json ("KsefConfig")
builder.Services.Configure<KsefSettings>(
    builder.Configuration.GetSection("KsefConfig"));

// Строка подключения (берем из конфига или дефолт)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Data Source=ksef_gateway.db";

builder.Services.AddDbContext<KsefContext>(options => 
    options.UseSqlite(connectionString));

// ===================================================
// 3. СЕРВИСЫ KSEF (CORE LOGIC)
// ===================================================
// Регистрируем типизированный клиент (это свяжет HttpClient и KsefClient)
builder.Services.AddHttpClient<KsefClient>();

// Основные сервисы
builder.Services.AddScoped<KsefAuthService>();
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<EncryptionService>(); // Если где-то еще используется
builder.Services.AddMemoryCache();

// Фоновый воркер (если он нужен для проверки статусов)
builder.Services.AddHostedService<KsefWorker>();

// ===================================================
// 4. WEB API И UI
// ===================================================
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Blazor (Interactive Server)
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// ===================================================
// 5. МИГРАЦИИ БД (AUTO-MIGRATION)
// ===================================================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
    try 
    {
        db.Database.Migrate();
        // WAL режим ускоряет SQLite и уменьшает блокировки
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        Console.WriteLine("--> Database migrated successfully (WAL mode enabled).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> !!! Database migration failed: {ex.Message}");
    }
}

// ===================================================
// 6. PIPELINE ЗАПРОСОВ
// ===================================================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles(); // Нужно для Blazor CSS/JS
app.UseAntiforgery();

app.UseAuthorization();

// Подключаем контроллеры API
app.MapControllers();

// Подключаем Blazor UI
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();