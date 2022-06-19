using CryptoExchange.Net.Objects;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public class JsonDataSerializer : IDataSerializer<string>
    {
        public Task<CallResult<string>> SerializeAsync<TInput>(TInput data)
        {
            var jsonData = JsonConvert.SerializeObject(data);
            return Task.FromResult(new CallResult<string>(jsonData));
        }
    }
}
