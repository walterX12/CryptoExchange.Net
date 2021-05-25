using System;

namespace CryptoExchange.Net.Sockets
{
    /// <summary>
    /// An update received from a socket update subscription
    /// </summary>
    /// <typeparam name="T">The type of the data</typeparam>
    public class DataEvent<T>
    {
        /// <summary>
        /// The timestamp the data was received
        /// </summary>
        public DateTime Timestamp { get; set; }
        /// <summary>
        /// The topic of the update, what symbol/asset etc..
        /// </summary>
        public string? Topic { get; set; }
        /// <summary>
        /// The original data that was received, only available when TODO
        /// </summary>
        public string? OriginalData { get; set; }
        /// <summary>
        /// The received data deserialized into an object
        /// </summary>
        public T Data { get; set; }

        public DataEvent(T data, DateTime timestamp)
        {
            Data = data;
            Timestamp = timestamp;
        }

        public DataEvent(T data, string? topic, DateTime timestamp)
        {
            Data = data;
            Topic = topic;
            Timestamp = timestamp;
        }

        public DataEvent(T data, string? topic, string? originalData, DateTime timestamp)
        {
            Data = data;
            Topic = topic;
            OriginalData = originalData;
            Timestamp = timestamp;
        }

        public DataEvent<K> Copy<K>(K data)
        {
            return new DataEvent<K>(data, Topic, OriginalData, Timestamp);
        }

        public DataEvent<K> Copy<K>(K data, string? topic)
        {
            return new DataEvent<K>(data, topic, OriginalData, Timestamp);
        }
    }
}
