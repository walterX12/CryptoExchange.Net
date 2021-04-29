using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Sockets
{
    internal class CryptoExchangeWebSocketClient : IWebsocket
    {
        internal static int lastStreamId;
        private static readonly object streamIdLock = new object();

        private ClientWebSocket _socket;
        private Task? _sendTask;
        private Task? _receiveTask;
        private AutoResetEvent _sendEvent;
        private ConcurrentQueue<byte[]> _sendBuffer;
        private readonly IDictionary<string, string> cookies;
        private readonly IDictionary<string, string> headers;
        private CancellationTokenSource _ctsSource;

        /// <summary>
        /// Log
        /// </summary>
        protected Log log;

        /// <summary>
        /// Error handlers
        /// </summary>
        protected readonly List<Action<Exception>> errorHandlers = new List<Action<Exception>>();
        /// <summary>
        /// Open handlers
        /// </summary>
        protected readonly List<Action> openHandlers = new List<Action>();
        /// <summary>
        /// Close handlers
        /// </summary>
        protected readonly List<Action> closeHandlers = new List<Action>();
        /// <summary>
        /// Message handlers
        /// </summary>
        protected readonly List<Action<string>> messageHandlers = new List<Action<string>>();

        public int Id { get; }

        public string? Origin { get; set; }
        public bool Reconnecting { get; set; }
        public DateTime LastActionTime { get; private set; }

        public Func<byte[], string>? DataInterpreterBytes { get; set; }
        public Func<string, string>? DataInterpreterString { get; set; }

        public string Url { get; }

        public WebSocketState SocketState => _socket.State;

        public bool IsClosed => _socket.State == WebSocketState.Closed;

        public bool IsOpen => _socket.State == WebSocketState.Open;

        public SslProtocols SSLProtocols { get; set; } //TODO
        public TimeSpan Timeout { get; set; } // TODO

        /// <summary>
        /// On close
        /// </summary>
        public event Action OnClose
        {
            add => closeHandlers.Add(value);
            remove => closeHandlers.Remove(value);
        }
        /// <summary>
        /// On message
        /// </summary>
        public event Action<string> OnMessage
        {
            add => messageHandlers.Add(value);
            remove => messageHandlers.Remove(value);
        }
        /// <summary>
        /// On error
        /// </summary>
        public event Action<Exception> OnError
        {
            add => errorHandlers.Add(value);
            remove => errorHandlers.Remove(value);
        }
        /// <summary>
        /// On open
        /// </summary>
        public event Action OnOpen
        {
            add => openHandlers.Add(value);
            remove => openHandlers.Remove(value);
        }

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="log"></param>
        /// <param name="url"></param>
        public CryptoExchangeWebSocketClient(Log log, string url) : this(log, url, new Dictionary<string, string>(), new Dictionary<string, string>())
        {
        }

        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="log"></param>
        /// <param name="url"></param>
        /// <param name="cookies"></param>
        /// <param name="headers"></param>
        public CryptoExchangeWebSocketClient(Log log, string url, IDictionary<string, string> cookies, IDictionary<string, string> headers)
        {
            Id = NextStreamId();
            this.log = log;
            Url = url;
            this.cookies = cookies;
            this.headers = headers;

            _sendEvent = new AutoResetEvent(false);
            _sendBuffer = new ConcurrentQueue<byte[]>();
            _ctsSource = new CancellationTokenSource();

            CreateSocket();
        }

        private void CreateSocket()
        {
            var cookieContainer = new CookieContainer();
            foreach (var cookie in cookies)
                cookieContainer.Add(new Cookie(cookie.Key, cookie.Value));

            _socket = new ClientWebSocket();
            _socket.Options.Cookies = cookieContainer;
            foreach (var header in headers)
                _socket.Options.SetRequestHeader(header.Key, header.Value);
            _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        }

        private async Task SendLoop()
        {
            log.Write(LogVerbosity.Debug, "Starting send loop");
            while (true)
            {
                _sendEvent.WaitOne();

                if (_socket.State != WebSocketState.Open)
                    break;

                if (!_sendBuffer.TryDequeue(out var data))
                    continue;

                try
                {
                    await _socket.SendAsync(new ArraySegment<byte>(data, 0, data.Length), WebSocketMessageType.Text, true, _ctsSource.Token);
                }
                catch (TaskCanceledException)
                {
                    // cancelled
                }
            }
            log.Write(LogVerbosity.Debug, "Ended send loop");
        }

        private async Task ReceiveLoop()
        {
            log.Write(LogVerbosity.Debug, "Starting receive loop");
            var buffer = new ArraySegment<byte>(new byte[2048]);
            while (true)
            {
                var memoryStream = new MemoryStream();
                WebSocketReceiveResult? receiveResult = null;
                while (true) 
                {
                    try
                    {
                        receiveResult = await _socket.ReceiveAsync(buffer, _ctsSource.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        // Cancelled
                        break;
                    }
                    catch(WebSocketException wse)
                    {
                        // Connection closed unexpectedly                        
                        _ctsSource.Cancel();
                        _sendEvent.Set();
                        await _sendTask;
                        Handle(closeHandlers);
                        break;
                    }

                    await memoryStream.WriteAsync(buffer.Array, buffer.Offset, receiveResult.Count);
                    if (receiveResult.EndOfMessage)
                        break;
                }

                if (receiveResult == null)
                    break;

                if (receiveResult.MessageType == WebSocketMessageType.Close || _ctsSource.IsCancellationRequested)
                    break;

                memoryStream.Seek(0, SeekOrigin.Begin);
                if (receiveResult.MessageType == WebSocketMessageType.Text) 
                {
                    var reader = new StreamReader(memoryStream, Encoding.UTF8);
                    Handle(messageHandlers, await reader.ReadToEndAsync());
                }
            }
            log.Write(LogVerbosity.Debug, "Ended receive loop");
        }

        public async Task Close()
        {
            log.Write(LogVerbosity.Debug, "Starting close");
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", default);
            _ctsSource.Cancel();
            _sendEvent.Set();
            await Task.WhenAll(_sendTask, _receiveTask);
            Handle(closeHandlers);
            log.Write(LogVerbosity.Debug, "Ended close");
        }

        public async Task<bool> Connect()
        {
            log.Write(LogVerbosity.Debug, "Starting connect");
            try
            {
                await _socket.ConnectAsync(new Uri(Url), default).ConfigureAwait(false);
                Handle(openHandlers);
            }
            catch(Exception e)
            {
                // TODO
                log.Write(LogVerbosity.Debug, "Connect failed: " + e.Message);
                return false;
            }

            log.Write(LogVerbosity.Debug, "Connected");
            _sendTask = Task.Run(async () => await SendLoop());
            _receiveTask = ReceiveLoop();
            return true;
        }

        public void Dispose()
        {
            log.Write(LogVerbosity.Debug, "Starting dispose");
            _socket.Dispose();
            log.Write(LogVerbosity.Debug, "Ended dispose");
        }

        public void Reset()
        {
            log.Write(LogVerbosity.Debug, "Starting reset");
            _ctsSource = new CancellationTokenSource();
            CreateSocket();
            log.Write(LogVerbosity.Debug, "Ended reset");
        }

        public void Send(string data)
        {
            if (_socket.State != WebSocketState.Open)
                throw new InvalidOperationException("Can't send data when socket is not connected");

            var bytes = Encoding.UTF8.GetBytes(data);
            _sendBuffer.Enqueue(bytes);
            _sendEvent.Set();
        }

        public void SetProxy(string host, int port) // Credentials?
        {
            _socket.Options.Proxy = new WebProxy(host, port);
        }

        /// <summary>
        /// Handle
        /// </summary>
        /// <param name="handlers"></param>
        protected void Handle(List<Action> handlers)
        {
            LastActionTime = DateTime.UtcNow;
            foreach (var handle in new List<Action>(handlers))
                handle?.Invoke();
        }

        /// <summary>
        /// Handle
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="handlers"></param>
        /// <param name="data"></param>
        protected void Handle<T>(List<Action<T>> handlers, T data)
        {
            LastActionTime = DateTime.UtcNow;
            foreach (var handle in new List<Action<T>>(handlers))
                handle?.Invoke(data);
        }

        private static int NextStreamId()
        {
            lock (streamIdLock)
            {
                lastStreamId++;
                return lastStreamId;
            }
        }
    }
}
