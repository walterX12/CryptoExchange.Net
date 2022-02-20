using CryptoExchange.Net.DataProcessors;
using CryptoExchange.Net.Objects;

namespace CryptoExchange.Net
{
    /// <summary>
    /// Base socket API client for interaction with a websocket API
    /// </summary>
    public abstract class SocketApiClient : BaseApiClient
    {
        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="options">The base client options</param>
        /// <param name="apiOptions">The Api client options</param>
        /// <param name="dataProcessor">The data processor</param>
        public SocketApiClient(BaseClientOptions options, ApiClientOptions apiOptions, IDataProcessor dataProcessor): base(options, apiOptions, dataProcessor)
        {
        }
    }
}
