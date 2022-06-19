using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public interface IDataSerializer<TOutput>
    {
        Task<CallResult<TOutput>> SerializeAsync<TInput>(TInput data);
    }
}
