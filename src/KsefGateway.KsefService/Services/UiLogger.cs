// src\KsefGateway.KsefService\Services\UiLogger.cs
using Microsoft.Extensions.Logging;

namespace KsefGateway.KsefService.Services
{
    // Провайдер - это фабрика, которая создает логгеры
    public class UiLoggerProvider : ILoggerProvider
    {
        private readonly LogService _logService;

        public UiLoggerProvider(LogService logService)
        {
            _logService = logService;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new UiLogger(_logService, categoryName);
        }

        public void Dispose() { }
    }

    // Сам логгер
    public class UiLogger : ILogger
    {
        private readonly LogService _logService;
        private readonly string _category;

        public UiLogger(LogService logService, string category)
        {
            _logService = logService;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Мы хотим видеть только логи от наших сервисов (Ksef...), чтобы не засорять эфир системным шумом
        public bool IsEnabled(LogLevel logLevel)
        {
            return _category.StartsWith("KsefGateway");
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            
            // Если есть ошибка, добавляем её текст
            if (exception != null)
            {
                message += $" | Error: {exception.Message}";
            }

            _logService.AddLog(logLevel.ToString(), message);
        }
    }
}