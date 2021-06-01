using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CryptoExchange.Net.Logging
{
    /// <summary>
    /// Log implementation
    /// </summary>
    public class Log
    {
        private List<ILogger> writers;
        /// <summary>
        /// The verbosity of the logging
        /// </summary>
        public LogLevel? Level { get; set; } = LogLevel.Information;

        /// <summary>
        /// Client name
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// ctor
        /// </summary>
        public Log(string clientName)
        {
            ClientName = clientName;
            writers = new List<ILogger>();
        }

        /// <summary>
        /// Set the writers
        /// </summary>
        /// <param name="textWriters"></param>
        public void UpdateWriters(List<ILogger> textWriters)
        {
            writers = textWriters;
        }

        /// <summary>
        /// Write a log entry
        /// </summary>
        /// <param name="logType"></param>
        /// <param name="message"></param>
        public void Write(LogLevel logType, string message)
        {
            if (Level != null && (int)logType < (int)Level)
                return;

            var logMessage = $"{ClientName,-10} | {message}";
            foreach (var writer in writers.ToList())
            {
                try
                {
                    writer.Log(logType, logMessage);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"Failed to write log to writer {writer.GetType()}: " + (e.InnerException?.Message ?? e.Message));
                }
            }
        }
    }
}
