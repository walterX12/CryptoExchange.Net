using CryptoExchange.Net.Objects;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public class JsonStringDeserializer : IDataDeserializer<string>
    {
        public Task<CallResult<TOutput>> DeserializeAsync<TOutput>(string input)
        {
            var data = JsonConvert.DeserializeObject<TOutput>(input);
            return Task.FromResult(new CallResult<TOutput>(data));
        }
    }
}
