using CryptoExchange.Net.Logging;
using CryptoExchange.Net.Objects;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoExchange.Net.DataProcessors
{
    public class JsonDataProcessor : IDataProcessor
    {
        private Func<string, Task<ServerError?>> _errorChecker;
        private Log _log;
        private JsonSerializer _serializer;

        public JsonDataProcessor(Log log, Func<string, Task<ServerError?>> errorChecker, JsonSerializer serializer)
        {
            _log = log;
            _errorChecker = errorChecker;
            _serializer = serializer;
        }

        public virtual Task<ServerError?> CheckForErrorAsync(string dataString)
        {
            return _errorChecker(dataString);
        }

        public virtual async Task<CallResult<T>> DeserializeAsync<T>(int id, Stream dataStream, CancellationToken ct)
        {
            try
            {
                // Let the reader keep the stream open so we're able to seek if needed. The calling method will close the stream.
                using var reader = new StreamReader(dataStream, Encoding.UTF8, false, 512, true);
                using var jsonReader = new JsonTextReader(reader);
                return new CallResult<T>(_serializer.Deserialize<T>(jsonReader)!);
            }
            catch (JsonReaderException jre)
            {
                string data;
                if (dataStream.CanSeek)
                {
                    // If we can seek the stream rewind it so we can retrieve the original data that was sent
                    dataStream.Seek(0, SeekOrigin.Begin);
                    data = await ReadStreamAsync(dataStream).ConfigureAwait(false);
                }
                else
                    data = "[Data only available in Debug LogLevel]";
                _log.Write(LogLevel.Error, $"[{id}] Deserialize JsonReaderException: {jre.Message}, Path: {jre.Path}, LineNumber: {jre.LineNumber}, LinePosition: {jre.LinePosition}, data: {data}");
                return new CallResult<T>(new DeserializeError($"Deserialize JsonReaderException: {jre.Message}, Path: {jre.Path}, LineNumber: {jre.LineNumber}, LinePosition: {jre.LinePosition}", data));
            }
            catch (JsonSerializationException jse)
            {
                string data;
                if (dataStream.CanSeek)
                {
                    dataStream.Seek(0, SeekOrigin.Begin);
                    data = await ReadStreamAsync(dataStream).ConfigureAwait(false);
                }
                else
                    data = "[Data only available in Debug LogLevel]";

                _log.Write(LogLevel.Error, $"[{id}] Deserialize JsonSerializationException: {jse.Message}, data: {data}");
                return new CallResult<T>(new DeserializeError($"Deserialize JsonSerializationException: {jse.Message}", data));
            }
            catch (Exception ex)
            {
                string data;
                if (dataStream.CanSeek)
                {
                    dataStream.Seek(0, SeekOrigin.Begin);
                    data = await ReadStreamAsync(dataStream).ConfigureAwait(false);
                }
                else
                    data = "[Data only available in Debug LogLevel]";

                var exceptionInfo = ex.ToLogString();
                _log.Write(LogLevel.Error, $"[{id}] Deserialize Unknown Exception: {exceptionInfo}, data: {data}");
                return new CallResult<T>(new DeserializeError($"Deserialize Unknown Exception: {exceptionInfo}", data));
            }
        }

        private static async Task<string> ReadStreamAsync(Stream stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, 512, true);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        public virtual Task<CallResult<T>> DeserializeAsync<T>(int id, string dataString, CancellationToken ct)
        {
            var tokenResult = ValidateJson(dataString);
            if (!tokenResult)
            {
                _log.Write(LogLevel.Error, tokenResult.Error!.Message);
                return Task.FromResult(new CallResult<T>(tokenResult.Error));
            }

            return Task.FromResult(DeserializeToken<T>(id, tokenResult.Data));
        }

        protected CallResult<T> DeserializeToken<T>(int id, JToken token)
        {
            try
            {
                return new CallResult<T>(token.ToObject<T>(_serializer)!);
            }
            catch (JsonReaderException jre)
            {
                var info = $"[{id}] Deserialize JsonReaderException: {jre.Message} Path: {jre.Path}, LineNumber: {jre.LineNumber}, LinePosition: {jre.LinePosition}, data: {token}";
                _log.Write(LogLevel.Error, info);
                return new CallResult<T>(new DeserializeError(info, token));
            }
            catch (JsonSerializationException jse)
            {
                var info = $"[{id}] Deserialize JsonSerializationException: {jse.Message} data: {token}";
                _log.Write(LogLevel.Error, info);
                return new CallResult<T>(new DeserializeError(info, token));
            }
            catch (Exception ex)
            {
                var exceptionInfo = ex.ToLogString();
                var info = $"[{id}] Deserialize Unknown Exception: {exceptionInfo}, data: {token}";
                _log.Write(LogLevel.Error, info);
                return new CallResult<T>(new DeserializeError(info, token));
            }
        }

        /// <summary>
        /// Tries to parse the json data and return a JToken, validating the input not being empty and being valid json
        /// </summary>
        /// <param name="data">The data to parse</param>
        /// <returns></returns>
        protected CallResult<JToken> ValidateJson(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                var info = "Empty data object received";
                _log.Write(LogLevel.Error, info);
                return new CallResult<JToken>(new DeserializeError(info, data));
            }

            try
            {
                return new CallResult<JToken>(JToken.Parse(data));
            }
            catch (JsonReaderException jre)
            {
                var info = $"Deserialize JsonReaderException: {jre.Message}, Path: {jre.Path}, LineNumber: {jre.LineNumber}, LinePosition: {jre.LinePosition}";
                return new CallResult<JToken>(new DeserializeError(info, data));
            }
            catch (JsonSerializationException jse)
            {
                var info = $"Deserialize JsonSerializationException: {jse.Message}";
                return new CallResult<JToken>(new DeserializeError(info, data));
            }
            catch (Exception ex)
            {
                var exceptionInfo = ex.ToLogString();
                var info = $"Deserialize Unknown Exception: {exceptionInfo}";
                return new CallResult<JToken>(new DeserializeError(info, data));
            }
        }
    }
}
