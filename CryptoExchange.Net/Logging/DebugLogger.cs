using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace CryptoExchange.Net.Logging
{
    /// <summary>
    /// Default log writer, writes to debug
    /// </summary>
    public class DebugLogger: ILogger
    {
        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            Trace.WriteLine(formatter(state, exception));
        }
    }
}
