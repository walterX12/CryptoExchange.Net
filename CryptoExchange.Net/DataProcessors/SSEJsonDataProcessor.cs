using CryptoExchange.Net.Logging;
using CryptoExchange.Net.Objects;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoExchange.Net.DataProcessors
{
    public class SSEJsonDataProcessor : JsonDataProcessor
    {
        public SSEJsonDataProcessor(Log log, Func<string, Task<ServerError?>> errorChecker, JsonSerializer serializer) : base(log, errorChecker, serializer)
        {
        }

        public override async Task<CallResult<T>> DeserializeAsync<T>(int id, string dataString, CancellationToken ct)
        {
            var result = Activator.CreateInstance(typeof(T));
            var lines = dataString.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for(var i = 0; i < lines.Length; i++)
            {
                var currentLine = lines[i];
                if (string.IsNullOrEmpty(currentLine))
                    continue;

                if (currentLine.StartsWith("event:"))
                {
                    var eventName = currentLine.Split(':')[1].Trim(' ');
                    if (eventName == "start" || eventName == "end")
                        continue;

                    var property = typeof(T).GetProperty(eventName.Substring(0, 1).ToUpper() + eventName.Substring(1));                   

                    var data = lines[i + 1].Substring(6).Trim(' ');
                    var token = ValidateJson(data);
                    if (!token.Success)
                        return new CallResult<T>(token.Error!);

                    var mi = typeof(JsonDataProcessor).GetMethod("DeserializeToken", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);                    
                    var fooRef = mi.GetBaseDefinition().MakeGenericMethod(property.PropertyType);
                    var desResult = (dynamic)fooRef.Invoke(this, new object[] { id, token.Data });

                    property.SetValue(result, desResult.Data);
                }
            }

            return new CallResult<T>((T)result);
        }
    }
}
