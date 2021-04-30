using CryptoExchange.Net.Interfaces;
using CryptoExchange.Net.Logging;
using CryptoExchange.Net.Objects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoExchange.Net.Sockets
{
    public class CryptoExchangeWebSocketClient : IWebsocket
    {
        internal static int lastStreamId;
        private static readonly object streamIdLock = new object();

        private ClientWebSocket _socket;
        private Task? _sendTask;
        private Task? _receiveTask;
        private Task? _timeoutTask;
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

        public bool IsClosed => _socket.State == WebSocketState.Closed;

        public bool IsOpen => _socket.State == WebSocketState.Open;

        public SslProtocols SSLProtocols { get; set; } //TODO

        private Encoding _encoding = Encoding.UTF8;
        public Encoding? Encoding
        {
            get => _encoding;
            set
            {
                if(value != null)
                    _encoding = value;
            }
        }

        public TimeSpan Timeout { get; set; }

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

        public virtual void SetProxy(ApiProxy proxy)
        {
            _socket.Options.Proxy = new WebProxy(proxy.Host, proxy.Port);
            if (proxy.Login != null)
                _socket.Options.Proxy.Credentials = new NetworkCredential(proxy.Login, proxy.Password);
        }

        public virtual async Task<bool> Connect()
        {
            log.Write(LogVerbosity.Debug, $"Socket {Id} connecting");
            try
            {
                using CancellationTokenSource tcs = new CancellationTokenSource(TimeSpan.FromSeconds(10));                
                await _socket.ConnectAsync(new Uri(Url), default).ConfigureAwait(false);
                
                Handle(openHandlers);
            }
            catch (Exception e)
            {
                log.Write(LogVerbosity.Debug, $"Socket {Id} connection failed: " + e.Message);
                return false;
            }

            log.Write(LogVerbosity.Debug, $"Socket {Id} connected");
            _sendTask = Task.Run(async () => await SendLoop().ConfigureAwait(false));
            _receiveTask = ReceiveLoop();
            if (Timeout != default)
                _timeoutTask = Task.Run(CheckTimeout);
            return true;
        }

        public virtual void Send(string data)
        {
            if (_socket.State != WebSocketState.Open)
                throw new InvalidOperationException("Can't send data when socket is not connected");

            var bytes = _encoding.GetBytes(data);
            _sendBuffer.Enqueue(bytes);
            _sendEvent.Set();
        }

        public virtual async Task Close()
        {
            log.Write(LogVerbosity.Debug, $"Socket {Id} closing");
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", default).ConfigureAwait(false);
            _ctsSource.Cancel();
            _sendEvent.Set();
            var tasks = new List<Task> { _sendTask, _receiveTask };
            if (_timeoutTask != null)
                tasks.Add(_timeoutTask);

            await Task.WhenAll(tasks).ConfigureAwait(false);
            Handle(closeHandlers);
            log.Write(LogVerbosity.Debug, $"Socket {Id} closed");
        }
        
        public void Dispose()
        {
            log.Write(LogVerbosity.Debug, $"Socket {Id} disposing");
            _socket.Dispose();
            _ctsSource.Dispose();

            errorHandlers.Clear();
            openHandlers.Clear();
            closeHandlers.Clear();
            messageHandlers.Clear();
        }

        public void Reset()
        {
            log.Write(LogVerbosity.Debug, $"Socket {Id} resetting");
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
            while (true)
            {
                _sendEvent.WaitOne();

                if (_socket.State != WebSocketState.Open)
                    break;

                if (!_sendBuffer.TryDequeue(out var data))
                    continue;

                try
                {
                    await _socket.SendAsync(new ArraySegment<byte>(data, 0, data.Length), WebSocketMessageType.Text, true, _ctsSource.Token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    // cancelled
                }
                catch (WebSocketException wse)
                {
                    // Connection closed unexpectedly                        
                    _ctsSource.Cancel();
                    await _receiveTask!.ConfigureAwait(false);
                    Handle(errorHandlers, wse);
                    Handle(closeHandlers);
                    break;
                }
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new ArraySegment<byte>(new byte[4096]);
            while (true)
            {
                if (_ctsSource.IsCancellationRequested)
                    break;

                MemoryStream? memoryStream = null;
                WebSocketReceiveResult? receiveResult = null;
                bool multiPartMessage = false;
                while (true)
                {
                    try
                    {
                        receiveResult = await _socket.ReceiveAsync(buffer, _ctsSource.Token).ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        // Cancelled
                        break;
                    }
                    catch (WebSocketException wse)
                    {
                        // Connection closed unexpectedly                        
                        _ctsSource.Cancel();
                        _sendEvent.Set();
                        await _sendTask!.ConfigureAwait(false);
                        Handle(errorHandlers, wse);
                        Handle(closeHandlers);
                        break;
                    }

                    if (!receiveResult.EndOfMessage)
                    {
                        // We received data, but it is not complete, write it to a memory stream
                        multiPartMessage = true;
                        if (memoryStream == null)
                            memoryStream = new MemoryStream();
                        await memoryStream.WriteAsync(buffer.Array, buffer.Offset, receiveResult.Count).ConfigureAwait(false);
                    }
                    else
                    {
                        if (!multiPartMessage)
                            // Received a complete message and it's not multi part
                            HandleMessage(buffer.Array, buffer.Offset, receiveResult.Count, receiveResult.MessageType);
                        else
                            // Received the end of a multipart message, write to memory stream
                            await memoryStream!.WriteAsync(buffer.Array, buffer.Offset, receiveResult.Count).ConfigureAwait(false);
                        break;
                    }
                }

                if (receiveResult?.MessageType == WebSocketMessageType.Close)
                    // Received close message
                    break;

                if (receiveResult == null || _ctsSource.IsCancellationRequested)
                    // Error during receiving or cancellation requested, stop.
                    break;

                if (multiPartMessage)
                {
                    // Reassemble complete message from memory stream
                    HandleMessage(memoryStream!.ToArray(), 0, (int)memoryStream.Length, receiveResult.MessageType);
                    memoryStream.Dispose();
                }
            }
        }

        private void HandleMessage(byte[] data, int offset, int count, WebSocketMessageType messageType)
        {
            string strData;
            if (messageType == WebSocketMessageType.Binary)
            {
                if (DataInterpreterBytes == null)
                    throw new Exception("Byte interpreter not set while receiving byte data");

                try
                {
                    strData = DataInterpreterBytes(data);
                }
                catch(Exception e)
                {
                    log.Write(LogVerbosity.Error, $"Socket {Id} unhandled exception during byte data interpretation: " + e.ToLogString());
                    return;
                }
            }
            else
                strData = _encoding.GetString(data, offset, count);

            if (DataInterpreterString != null)
            {
                try
                {
                    strData = DataInterpreterString(strData);
                }
                catch(Exception e)
                {
                    log.Write(LogVerbosity.Error, $"Socket {Id} unhandled exception during byte data interpretation: " + e.ToLogString());
                    return;
                }
            }

            try
            {
                Handle(messageHandlers, strData);
            }
            catch(Exception e)
            {
                log.Write(LogVerbosity.Error, $"Socket {Id} unhandled exception during message processing: " + e.ToLogString());
                return;
            }
        }

        /// <summary>
        /// Checks if timed out
        /// </summary>
        /// <returns></returns>
        protected async Task CheckTimeout()
        {
            while (true)
            {
                if (_socket.State != WebSocketState.Open)
                    return;

                if (DateTime.UtcNow - LastActionTime > Timeout)
                {
                    log.Write(LogVerbosity.Warning, $"No data received for {Timeout}, reconnecting socket");
                    _ = Close().ConfigureAwait(false);
                    return;
                }
                try
                {
                    await Task.Delay(500, _ctsSource.Token).ConfigureAwait(false);
                }
                catch(TaskCanceledException)
                {
                    // cancelled
                    return;
                }
            }
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
