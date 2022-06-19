using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public interface IDataSender
    {
        Task<CallResult<TOutput>> SendAsync<TInput, TOutput>(RequestData<TInput> data);
    }
}
