using System.Security.Authentication;
using KsefGateway.KsefService.Configuration;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- 1. РЕГИСТРАЦИЯ СЕРВИСОВ ---

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthorization();

// Настройки
builder.Services.Configure<KsefSettings>(
    builder.Configuration.GetSection("KsefConfig"));

// База данных
builder.Services.AddDbContext<KsefContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// === ВАЖНЫЙ БЛОК: НАСТРОЙКА КЛИЕНТА KSEF ===
builder.Services.AddHttpClient<KsefClient>(client => 
{
    // 1. Притворяемся браузером (KSeF не любит пустой User-Agent)
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // 2. Включаем современные протоколы TLS
    SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
    
    // 3. ОТКЛЮЧАЕМ ПРОВЕРКУ СЕРТИФИКАТА (Игнорируем вмешательство Антивируса)
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});
// ===========================================

var app = builder.Build();

// --- 2. НАСТРОЙКА КОНВЕЙЕРА ---

app.UseSwagger();
app.UseSwaggerUI(c => 
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "KSeF Gateway API v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthorization();
app.MapControllers();

app.Run();