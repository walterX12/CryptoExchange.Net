using CryptoExchange.Net.Objects;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public class JsonStreamDeserializer : IDataDeserializer<Stream>
    {
        private JsonSerializer _serializer;

        public JsonStreamDeserializer()
        {
            _serializer = new JsonSerializer();
        }

        public Task<CallResult<TOutput>> DeserializeAsync<TOutput>(Stream input)
        {
            using var reader = new StreamReader(input, Encoding.UTF8, false, 512, true);
            using var jsonReader = new JsonTextReader(reader);
            return Task.FromResult(new CallResult<TOutput>(_serializer.Deserialize<TOutput>(jsonReader)!));
        }
    }
}
