// src\KsefGateway.KsefService\Services\LogService.cs
using System;
using System.Collections.Concurrent;

namespace KsefGateway.KsefService.Services
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "";
        public string Message { get; set; } = "";
        public string ColorClass => Level switch
        {
            "Error" or "Critical" => "text-danger", // Красный
            "Warning" => "text-warning",            // Желтый
            "Information" => "text-success",        // Зеленый
            _ => "text-muted"
        };
    }

    public class LogService
    {
        // Храним последние 50 сообщений
        private readonly ConcurrentQueue<LogEntry> _logs = new();
        private const int MaxLogs = 50;

        // Событие: "Эй, появился новый лог!"
        public event Action? OnChange;

        public void AddLog(string level, string message)
        {
            var entry = new LogEntry 
            { 
                Timestamp = DateTime.Now, 
                Level = level, 
                Message = message 
            };

            _logs.Enqueue(entry);

            // Чистим старые, чтобы память не текла
            if (_logs.Count > MaxLogs) _logs.TryDequeue(out _);

            // Сообщаем интерфейсу об обновлении
            OnChange?.Invoke();
        }

        public LogEntry[] GetLogs()
        {
            return _logs.ToArray().Reverse().ToArray(); // Новые сверху
        }
    }
}