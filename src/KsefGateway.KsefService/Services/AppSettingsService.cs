//  src\KsefGateway.KsefService\Services\AppSettingsService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using KsefGateway.KsefService.Data;
using KsefGateway.KsefService.Data.Entities;
using KsefGateway.KsefService.Configuration;
using Microsoft.Extensions.Options;

namespace KsefGateway.KsefService.Services
{
    public class AppSettingsService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _cache;
        private readonly KsefSettings _defaultSettings;

        public AppSettingsService(
            IServiceProvider serviceProvider, 
            IMemoryCache cache,
            IOptions<KsefSettings> defaultOptions)
        {
            _serviceProvider = serviceProvider;
            _cache = cache;
            _defaultSettings = defaultOptions.Value;
        }

        // === ПОЛУЧЕНИЕ НАСТРОЙКИ (С КЭШИРОВАНИЕМ) ===
        public async Task<string> GetValueAsync(string key)
        {
            // 1. Пытаемся найти в кэше (память)
            if (_cache.TryGetValue<string>(key, out var cachedValue))
            {
                return cachedValue!;
            }

            // 2. Если нет - идем в БД (создаем Scope, так как EF Context не ThreadSafe)
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
                var setting = await db.Settings.FindAsync(key);

                string value;
                if (setting != null)
                {
                    value = setting.Value;
                }
                else
                {
                    // Если в БД пусто, берем из appsettings.json (как дефолт) и СОХРАНЯЕМ в БД
                    value = GetDefaultFromConfig(key);
                    if (!string.IsNullOrEmpty(value))
                    {
                        db.Settings.Add(new AppSetting { Key = key, Value = value });
                        await db.SaveChangesAsync();
                    }
                }

                // 3. Сохраняем в кэш на 5 минут
                _cache.Set(key, value, TimeSpan.FromMinutes(5));
                return value;
            }
        }

        // === ОБНОВЛЕНИЕ НАСТРОЙКИ (HOT UPDATE) ===
        public async Task SetValueAsync(string key, string value)
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KsefContext>();
                var setting = await db.Settings.FindAsync(key);

                if (setting == null)
                {
                    setting = new AppSetting { Key = key, Value = value };
                    db.Settings.Add(setting);
                }
                else
                {
                    setting.Value = value;
                }

                await db.SaveChangesAsync();
            }

            // Сбрасываем кэш, чтобы все сервисы сразу увидели новое значение
            _cache.Remove(key);
        }

        // === ИСПРАВЛЕННЫЙ МЕТОД: Маппинг старых ключей на новые свойства ===
        private string GetDefaultFromConfig(string key)
        {
            return key switch
            {
                // Логика: Если просят NIP, пытаемся вырезать его из ApiToken (542...|Hash)
                "Ksef:Nip" => _defaultSettings.ApiToken.Contains('|') 
                              ? _defaultSettings.ApiToken.Split('|')[0] 
                              : "5423240211", // Fallback если токена нет

                // Логика: AuthToken теперь равен ApiToken
                "Ksef:AuthToken" => _defaultSettings.ApiToken,

                // Базовый URL без изменений
                "Ksef:BaseUrl" => _defaultSettings.BaseUrl,

                // Остальные ключи
                "Ksef:IdentifierType" => "onip", // Стандарт для Gateway, хотя v2 использует Nip
                
                _ => string.Empty
            };
        }
    }
}