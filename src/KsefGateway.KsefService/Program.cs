// src/KsefGateway.KsefService/Program.cs
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services;
using Microsoft.EntityFrameworkCore;
using KsefGateway.KsefService.Components;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// === 1. ЛОГИРОВАНИЕ ===
builder.Services.AddSingleton<LogService>();
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.AddSingleton<ILoggerProvider, UiLoggerProvider>();

// === 2. СЕРВИСЫ ===
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();
builder.Services.Configure<KsefSettings>(builder.Configuration.GetSection("Ksef"));

// База данных
builder.Services.AddDbContext<KsefContext>(options => 
    options.UseSqlite("Data Source=ksef_gateway.db"));

// Http Client
builder.Services.AddHttpClient();

// Кэш (ОБЯЗАТЕЛЬНО для AppSettingsService)
builder.Services.AddMemoryCache();

// Наши сервисы
builder.Services.AddScoped<AppSettingsService>();
builder.Services.AddScoped<KsefAuthService>();

// === 3. ФОНОВЫЕ ЗАДАЧИ (ОТКЛЮЧЕНО НА ВРЕМЯ ОТЛАДКИ) ===
// builder.Services.AddHostedService<KsefWorker>(); // <--- ЗАКОММЕНТИРОВАНО!

// === 4. UI (BLAZOR) ===
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// === 5. PIPELINE ===
// Включение Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthorization();
app.MapControllers();

// Blazor Dashboard
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Инициализация БД
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
    try 
    {
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;"); // Ускоряет SQLite
        Console.WriteLine("--> Database ready (WAL mode).");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"--> DB Error: {ex.Message}");
    }
}

app.Run();