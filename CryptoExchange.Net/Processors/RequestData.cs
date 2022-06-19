using CryptoExchange.Net.Objects;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;

namespace CryptoExchange.Net.Processors
{
    public class RequestData<T>
    {
        public T Data { get; set; }
    }

    public class RestRequestData: RequestData<Dictionary<string, object>>
    {
        public Uri Address { get; set; }
        public HttpMethod Method { get; set; }
        public HttpMethodParameterPosition ParameterPosition { get; set; }
    }
}
