using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Processors;
using CryptoExchange.Net.Requests;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CryptoExchange.Net
{
    /// <summary>
    /// Base rest client
    /// </summary>
    public abstract class BaseRestClient : BaseClient, IRestClient
    {
        /// <summary>
        /// The factory for creating requests. Used for unit testing
        /// </summary>
        public IRequestFactory RequestFactory { get; set; } = new RequestFactory();
        
        /// <inheritdoc />
        public int TotalRequestsMade => ApiClients.OfType<RestApiClient>().Sum(s => s.TotalRequestsMade);

        /// <summary>
        /// Request headers to be sent with each request
        /// </summary>
        protected Dictionary<string, string>? StandardRequestHeaders { get; set; }

        /// <summary>
        /// Client options
        /// </summary>
        public new BaseRestClientOptions ClientOptions { get; }

        private RestDataHandler _dataHandler;

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="name">The name of the API this client is for</param>
        /// <param name="options">The options for this client</param>
        protected BaseRestClient(string name, BaseRestClientOptions options) : base(name, options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _dataHandler = new RestDataHandler(new UrlParametersSerializer(), new JsonStreamDeserializer(), options.HttpClient ?? new HttpClient());
            ClientOptions = options;
            RequestFactory.Configure(options.RequestTimeout, options.Proxy, options.HttpClient);
        }

        /// <inheritdoc />
        public void SetApiCredentials(ApiCredentials credentials)
        {
            foreach (var apiClient in ApiClients)
                apiClient.SetApiCredentials(credentials);
        }

        /// <summary>
        /// Execute a request to the uri and returns if it was successful
        /// </summary>
        /// <param name="apiClient">The API client the request is for</param>
        /// <param name="uri">The uri to send the request to</param>
        /// <param name="method">The method of the request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="parameters">The parameters of the request</param>
        /// <param name="signed">Whether or not the request should be authenticated</param>
        /// <param name="parameterPosition">Where the parameters should be placed, overwrites the value set in the client</param>
        /// <param name="arraySerialization">How array parameters should be serialized, overwrites the value set in the client</param>
        /// <param name="requestWeight">Credits used for the request</param>
        /// <param name="deserializer">The JsonSerializer to use for deserialization</param>
        /// <param name="additionalHeaders">Additional headers to send with the request</param>
        /// <param name="ignoreRatelimit">Ignore rate limits for this request</param>
        /// <returns></returns>
        [return: NotNull]
        protected virtual async Task<WebCallResult> SendRequestAsync(RestApiClient apiClient,
            Uri uri,
            HttpMethod method,
            CancellationToken cancellationToken,
            Dictionary<string, object>? parameters = null,
            bool signed = false,
            HttpMethodParameterPosition? parameterPosition = null,
            ArrayParametersSerialization? arraySerialization = null,
            int requestWeight = 1,
            Dictionary<string, string>? additionalHeaders = null,
            bool ignoreRatelimit = false)
        {
            var request = await PrepareRequestAsync(apiClient, uri, method, cancellationToken, parameters, signed, parameterPosition, arraySerialization, requestWeight, additionalHeaders, ignoreRatelimit).ConfigureAwait(false);
            if (!request)
                return new WebCallResult(request.Error!);

            var result = (WebCallResult<object>)await _dataHandler.SendAsync<Dictionary<string, object>, object>(new RestRequestData
            {
                Address = request.Data.Uri,
                Method = request.Data.Method,
                ParameterPosition = parameterPosition ?? HttpMethodParameterPosition.InUri,
                Data = parameters
            }).ConfigureAwait(false);
            return result.AsDataless();
            //var result = await GetResponseAsync<object>(apiClient, request.Data, cancellationToken, true).ConfigureAwait(false);
            //return result.AsDataless();
        }

        /// <summary>
        /// Execute a request to the uri and deserialize the response into the provided type parameter
        /// </summary>
        /// <typeparam name="T">The type to deserialize into</typeparam>
        /// <param name="apiClient">The API client the request is for</param>
        /// <param name="uri">The uri to send the request to</param>
        /// <param name="method">The method of the request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="parameters">The parameters of the request</param>
        /// <param name="signed">Whether or not the request should be authenticated</param>
        /// <param name="parameterPosition">Where the parameters should be placed, overwrites the value set in the client</param>
        /// <param name="arraySerialization">How array parameters should be serialized, overwrites the value set in the client</param>
        /// <param name="requestWeight">Credits used for the request</param>
        /// <param name="deserializer">The JsonSerializer to use for deserialization</param>
        /// <param name="additionalHeaders">Additional headers to send with the request</param>
        /// <param name="ignoreRatelimit">Ignore rate limits for this request</param>
        /// <returns></returns>
        [return: NotNull]
        protected virtual async Task<WebCallResult<T>> SendRequestAsync<T>(
            RestApiClient apiClient,
            Uri uri, 
            HttpMethod method, 
            CancellationToken cancellationToken,            
            Dictionary<string, object>? parameters = null, 
            bool signed = false, 
            HttpMethodParameterPosition? parameterPosition = null,
            ArrayParametersSerialization? arraySerialization = null, 
            int requestWeight = 1,
            Dictionary<string, string>? additionalHeaders = null,
            bool ignoreRatelimit = false
            ) where T : class
        {
            var request = await PrepareRequestAsync(apiClient, uri, method, cancellationToken, parameters, signed, parameterPosition, arraySerialization, requestWeight, additionalHeaders, ignoreRatelimit).ConfigureAwait(false);
            if (!request)
                return new WebCallResult<T>(request.Error!);

            return (WebCallResult<T>)await _dataHandler.SendAsync<Dictionary<string, object>, T>(new RestRequestData
            {
                Address = request.Data.Uri,
                Method = request.Data.Method,
                ParameterPosition = parameterPosition ?? HttpMethodParameterPosition.InUri,
                Data = parameters
            }).ConfigureAwait(false);

            //return await GetResponseAsync<T>(apiClient, request.Data, cancellationToken, false).ConfigureAwait(false);
        }

        /// <summary>
        /// Prepares a request to be sent to the server
        /// </summary>
        /// <param name="apiClient">The API client the request is for</param>
        /// <param name="uri">The uri to send the request to</param>
        /// <param name="method">The method of the request</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="parameters">The parameters of the request</param>
        /// <param name="signed">Whether or not the request should be authenticated</param>
        /// <param name="parameterPosition">Where the parameters should be placed, overwrites the value set in the client</param>
        /// <param name="arraySerialization">How array parameters should be serialized, overwrites the value set in the client</param>
        /// <param name="requestWeight">Credits used for the request</param>
        /// <param name="additionalHeaders">Additional headers to send with the request</param>
        /// <param name="ignoreRatelimit">Ignore rate limits for this request</param>
        /// <returns></returns>
        protected virtual async Task<CallResult<IRequest>> PrepareRequestAsync(RestApiClient apiClient,
            Uri uri,
            HttpMethod method,
            CancellationToken cancellationToken,
            Dictionary<string, object>? parameters = null,
            bool signed = false,
            HttpMethodParameterPosition? parameterPosition = null,
            ArrayParametersSerialization? arraySerialization = null,
            int requestWeight = 1,
            Dictionary<string, string>? additionalHeaders = null,
            bool ignoreRatelimit = false)
        {
            var requestId = NextId();

            if (signed)
            {
                var syncTask = apiClient.SyncTimeAsync();
                var timeSyncInfo = apiClient.GetTimeSyncInfo();
                if (timeSyncInfo.TimeSyncState.LastSyncTime == default)
                {
                    // Initially with first request we'll need to wait for the time syncing, if it's not the first request we can just continue
                    var syncTimeResult = await syncTask.ConfigureAwait(false);
                    if (!syncTimeResult)
                    {
                        log.Write(LogLevel.Debug, $"[{requestId}] Failed to sync time, aborting request: " + syncTimeResult.Error);
                        return syncTimeResult.As<IRequest>(default);
                    }
                }
            }

            if (!ignoreRatelimit)
            {
                foreach (var limiter in apiClient.RateLimiters)
                {
                    var limitResult = await limiter.LimitRequestAsync(log, uri.AbsolutePath, method, signed, apiClient.Options.ApiCredentials?.Key, apiClient.Options.RateLimitingBehaviour, requestWeight, cancellationToken).ConfigureAwait(false);
                    if (!limitResult.Success)
                        return new CallResult<IRequest>(limitResult.Error!);
                }
            }

            if (signed && apiClient.AuthenticationProvider == null)
            {
                log.Write(LogLevel.Warning, $"[{requestId}] Request {uri.AbsolutePath} failed because no ApiCredentials were provided");
                return new CallResult<IRequest>(new NoApiCredentialsError());
            }

            log.Write(LogLevel.Information, $"[{requestId}] Creating request for " + uri);
            var paramsPosition = parameterPosition ?? apiClient.ParameterPositions[method];
            var request = ConstructRequest(apiClient, uri, method, parameters, signed, paramsPosition, arraySerialization ?? apiClient.arraySerialization, requestId, additionalHeaders);

            string? paramString = "";
            if (paramsPosition == HttpMethodParameterPosition.InBody)
                paramString = $" with request body '{request.Content}'";

            var headers = request.GetHeaders();
            if (headers.Any())
                paramString += " with headers " + string.Join(", ", headers.Select(h => h.Key + $"=[{string.Join(",", h.Value)}]"));

            apiClient.TotalRequestsMade++;
            log.Write(LogLevel.Trace, $"[{requestId}] Sending {method}{(signed ? " signed" : "")} request to {request.Uri}{paramString ?? " "}{(ClientOptions.Proxy == null ? "" : $" via proxy {ClientOptions.Proxy.Host}")}");
            return new CallResult<IRequest>(request);
        }



        /// <summary>
        /// Executes the request and returns the result deserialized into the type parameter class
        /// </summary>
        /// <param name="apiClient">The client making the request</param>
        /// <param name="request">The request object to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="expectedEmptyResponse">If an empty response is expected</param>
        /// <returns></returns>
        //protected virtual async Task<WebCallResult<T>> GetResponseAsync<T>(
        //    BaseApiClient apiClient, 
        //    IRequest request, 
        //    CancellationToken cancellationToken,
        //    bool expectedEmptyResponse)
        //{
        //}

        /// <summary>
        /// Creates a request object
        /// </summary>
        /// <param name="apiClient">The API client the request is for</param>
        /// <param name="uri">The uri to send the request to</param>
        /// <param name="method">The method of the request</param>
        /// <param name="parameters">The parameters of the request</param>
        /// <param name="signed">Whether or not the request should be authenticated</param>
        /// <param name="parameterPosition">Where the parameters should be placed</param>
        /// <param name="arraySerialization">How array parameters should be serialized</param>
        /// <param name="requestId">Unique id of a request</param>
        /// <param name="additionalHeaders">Additional headers to send with the request</param>
        /// <returns></returns>
        protected virtual IRequest ConstructRequest(
            RestApiClient apiClient,
            Uri uri,
            HttpMethod method,
            Dictionary<string, object>? parameters,
            bool signed,
            HttpMethodParameterPosition parameterPosition,
            ArrayParametersSerialization arraySerialization,
            int requestId,
            Dictionary<string, string>? additionalHeaders)
        {
            parameters ??= new Dictionary<string, object>();

            for (var i = 0; i< parameters.Count; i++)
            {
                var kvp = parameters.ElementAt(i);
                if (kvp.Value is Func<object> delegateValue)
                    parameters[kvp.Key] = delegateValue();
            }

            if (parameterPosition == HttpMethodParameterPosition.InUri)
            {
                foreach (var parameter in parameters)
                    uri = uri.AddQueryParmeter(parameter.Key, parameter.Value.ToString());
            }

            var headers = new Dictionary<string, string>();
            var uriParameters = parameterPosition == HttpMethodParameterPosition.InUri ? new SortedDictionary<string, object>(parameters) : new SortedDictionary<string, object>();
            var bodyParameters = parameterPosition == HttpMethodParameterPosition.InBody ? new SortedDictionary<string, object>(parameters) : new SortedDictionary<string, object>();
            if (apiClient.AuthenticationProvider != null)
                apiClient.AuthenticationProvider.AuthenticateRequest(
                    apiClient, 
                    uri, 
                    method, 
                    parameters, 
                    signed, 
                    arraySerialization,
                    parameterPosition,
                    out uriParameters, 
                    out bodyParameters, 
                    out headers);
                 
            // Sanity check
            foreach(var param in parameters)
            {
                if (!uriParameters.ContainsKey(param.Key) && !bodyParameters.ContainsKey(param.Key))
                    throw new Exception($"Missing parameter {param.Key} after authentication processing. AuthenticationProvider implementation " +
                        $"should return provided parameters in either the uri or body parameters output");
            }

            // Add the auth parameters to the uri, start with a new URI to be able to sort the parameters including the auth parameters            
            uri = uri.SetParameters(uriParameters, arraySerialization);
        
            var request = RequestFactory.Create(method, uri, requestId);
            request.Accept = Constants.JsonContentHeader;

            foreach (var header in headers)
                request.AddHeader(header.Key, header.Value);

            if (additionalHeaders != null)
            {
                foreach (var header in additionalHeaders)
                    request.AddHeader(header.Key, header.Value);
            }

            if (StandardRequestHeaders != null)
            {
                foreach (var header in StandardRequestHeaders)
                    // Only add it if it isn't overwritten
                    if (additionalHeaders?.ContainsKey(header.Key) != true)
                        request.AddHeader(header.Key, header.Value);
            }

            if (parameterPosition == HttpMethodParameterPosition.InBody)
            {
                //var contentType = apiClient.requestBodyFormat == RequestBodyFormat.Json ? Constants.JsonContentHeader : Constants.FormContentHeader;
                //if (bodyParameters.Any())
                //    WriteParamBody(apiClient, request, bodyParameters, contentType);
                //else
                //    request.SetContent(apiClient.requestBodyEmptyContent, contentType);
            }

            return request;
        }
    }
}
