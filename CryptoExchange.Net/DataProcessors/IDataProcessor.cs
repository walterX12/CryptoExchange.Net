using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoExchange.Net.DataProcessors
{
    public interface IDataProcessor
    {
        Task<ServerError?> CheckForErrorAsync(string dataString);
        Task<CallResult<T>> DeserializeAsync<T>(int id, Stream dataStream, CancellationToken ct);
        Task<CallResult<T>> DeserializeAsync<T>(int id, string dataString, CancellationToken ct);
    }
}
