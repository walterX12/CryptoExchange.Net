using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public interface IDataReceiver
    {
        Task<CallResult<T>> ReceiveAsync<T>();
    }
}
