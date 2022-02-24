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
        CallResult<T> Deserialize<T>(int id, Stream dataStream, CancellationToken ct);
        Task<CallResult<T>> DeserializeAsync<T>(int id, Stream dataStream, CancellationToken ct);
        CallResult<T> Deserialize<T>(int id, string dataString, CancellationToken ct);
        Task<CallResult<T>> DeserializeAsync<T>(int id, string dataString, CancellationToken ct);
        CallResult<string> Serialize<T>(int id, T data, CancellationToken ct);
        Task<CallResult<string>> SerializeAsync<T>(int id, T data, CancellationToken ct);
    }
}
