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

        private string GetDefaultFromConfig(string key)
        {
            return key switch
            {
                "Ksef:Nip" => _defaultSettings.Nip,
                "Ksef:AuthToken" => _defaultSettings.AuthToken,
                "Ksef:BaseUrl" => _defaultSettings.BaseUrl,
                _ => string.Empty
            };
        }
    }
}