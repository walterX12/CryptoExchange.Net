using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace CryptoExchange.Net.Logging
{
    public class ConsoleLogger : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var logMessage = $"{DateTime.Now:yyyy/MM/dd HH:mm:ss:fff} | {logLevel} | {formatter(state, exception)}";
            Console.WriteLine(logMessage);
        }
    }
}
