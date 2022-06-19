using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Processors
{
    public class RestDataHandler : IDataSender
    {
        private IDataSerializer<string> _serializer;
        private IDataDeserializer<Stream> _deserializer;
        private HttpClient _httpClient;

        public RestDataHandler(
            IDataSerializer<string> serializer, 
            IDataDeserializer<Stream> deserializer,
            HttpClient client)
        {
            _serializer = serializer;
            _deserializer = deserializer;
            _httpClient = client;
        }

        public async Task<CallResult<TOutput>> SendAsync<TInput, TOutput>(RequestData<TInput> data)
        {
            if (!(data is RestRequestData restRequestData))
                throw new ArgumentException("Invalid request data type for RestDataHandler");

            var serialized = await _serializer.SerializeAsync(restRequestData.Data).ConfigureAwait(false);
            if (!serialized)
            {
                return serialized.As<TOutput>(default);
            }

            var requestMessage = new HttpRequestMessage(restRequestData.Method, restRequestData.Address);
            if (serialized.Data != string.Empty)
            {
                if (restRequestData.ParameterPosition == HttpMethodParameterPosition.InBody)
                    requestMessage.Content = new StringContent(serialized.Data, Encoding.UTF8);
                else
                    requestMessage.RequestUri = new Uri(requestMessage.RequestUri.ToString().TrimEnd('/') + "/" + serialized.Data);
            }

            var sw = Stopwatch.StartNew();
            var result = await _httpClient.SendAsync(requestMessage).ConfigureAwait(false);

            var stream = await result.Content.ReadAsStreamAsync().ConfigureAwait(false);
            sw.Stop();
            var deserialized = await _deserializer.DeserializeAsync<TOutput>(stream).ConfigureAwait(false);
            if (!deserialized)
            {
                return deserialized.As<TOutput>(default);
            }

            return new WebCallResult<TOutput>(result.StatusCode, result.Headers, sw.Elapsed, null, restRequestData.Address.ToString(), null, restRequestData.Method, result.Headers, deserialized.Data, null);
        }
    }
}
