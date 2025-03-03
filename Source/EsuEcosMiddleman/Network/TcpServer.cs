// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EsuEcosMiddleman.Utilities;

namespace EsuEcosMiddleman.Network
{
    public delegate void ClientConnected(object sender, ITcpClient client);
    public delegate void ClientFailed(object sender, MessageEventArgs eventArgs);
    public delegate void MessageReceived(object sender, MessageEventArgs eventArgs);
    public delegate void SendFailed(object sender, MessageEventArgs eventArgs);

    public class TcpServer(CfgServer cfgServer = null) : ITcpServer, IExchange
    {
        public event EventHandler Stopped;
        public event ClientConnected ClientConnected;
        public event ClientFailed ClientFailed;
        public event MessageReceived MessageReceived;
        public event SendFailed SendFailed;

        private TcpListener _tcpServer;
        private Thread _serverThread;

        public ILogger Logger { get; set; }

        public ConcurrentBag<ITcpClient> ConnectedClients { get; private set; } = new();

        public CfgServer GetCfg()
        {
            return cfgServer;
        }

        public void Listen()
        {
            _stopped = false;

            if (_serverThread != null && _serverThread.IsAlive)
                return;

            var ip = cfgServer.ListenIp;
            var port = cfgServer.ListenPort;

            _tcpServer = new TcpListener(ip, port);
            _tcpServer.Start();

            _serverThread = new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;

                ListenInternal();
            });

            _serverThread.Start();
        }

        private void ListenInternal()
        {
            try
            {
                Logger?.Log?.Info($"<TcpServer> Listen started... (Port: {cfgServer.ListenPort})");
                
                while (true)
                {
                    var client = _tcpServer.AcceptTcpClient();
                    var t = new Thread(HandleDevice);
                    t.Start(client);
                }
            }
            catch (SocketException e)
            {
                Logger?.Log?.Error($"Listen failed: {e}");

                _tcpServer.Stop();
            }
        }

        private bool _stopped;

        public void Stop()
        {
            if (_stopped) return;

            _stopped = true;

            foreach (var itClient in ConnectedClients)
            {
                if (itClient == null) continue;
                if (!itClient.IsConnected) continue;

                try
                {
                    itClient.SendMessage("Quit\r\n\r\n");
                    itClient.Disconnect();
                }
                catch
                {
                    // ignore
                }
            }

            while (!ConnectedClients.IsEmpty)
                ConnectedClients.TryTake(out _);

            Stopped?.Invoke(this, null!);
        }

        public void HandleDevice(object obj)
        {
            var nativeClient = obj as System.Net.Sockets.TcpClient;
            if (nativeClient == null)
            {
                Logger?.Log?.Warn($"Invalid client connected");
                return;
            }

            var client = new TcpClient
            {
                Logger = Logger,
                NativeClient = nativeClient
            };

            if (_stopped)
            {
                // TODO send message that the service is stopped

                return;
            }

            ConnectedClients.Add(client);

            var threadClient = new Thread(async () =>
            {
                Thread.CurrentThread.IsBackground = true;

                await StartHandler(client);
            });

            client.ThreadInstance = threadClient;

            threadClient.Start();

            var ipaddr = client.Ip;
            var port = client.Port;

            Logger?.Log?.Info($"<Connector> Connection established from {ipaddr}:{port}");

            ClientConnected?.Invoke(this, client);
        }

        private async Task StartHandler(TcpClient client)
        {
            if (client == null) return;

            try
            {
                client.LineReceived += (_, line) =>
                {
                    if (string.IsNullOrEmpty(line)) return;
                    Logger?.Log?.Debug($"<TcpServer> Recv ({line.Length}): {line}");
                    MessageReceived?.Invoke(client, new MessageEventArgs(line));
                };

                client.SendFailed += (_, ex) =>
                {
                    Logger?.Log?.Error($"<TcpServer> Send failed: {ex.Message}");
                    SendFailed?.Invoke(client, new MessageEventArgs($"Send of message to {ex.Message}"));
                };

                client.Disconnected += (sender, _) =>
                {
                    var tcpClient = sender as ITcpClient;
                    if (tcpClient == null) return;

                    ConnectedClients = new ConcurrentBag<ITcpClient>(ConnectedClients.Except(new[] { tcpClient }));
                };

                await client.HandleLines();
            }
            catch (Exception ex)
            {
                ClientFailed?.Invoke(client, new MessageEventArgs($"{ex.Message}"));
            }
        }

        private readonly ConcurrentQueue<string> _messageQueue = new();
        private readonly SemaphoreSlim _queueLock = new(1, 1);

        /// <summary>
        /// Adds a message to the queue and ensures it gets sent sequentially.
        /// This prevents message interleaving when multiple senders use the same stream.
        /// </summary>
        /// <param name="msg">The message to send.</param>
        /// <param name="forceBase64Encode">If true, encodes the message in Base64 before sending.</param>
        public void SendMessage(string msg, bool forceBase64Encode = true)
        {
            if (string.IsNullOrEmpty(msg)) return;

            var encodedMsg = msg;
            if (!encodedMsg.IsBase64String() && forceBase64Encode)
                encodedMsg = System.Text.Encoding.UTF8.ToBase64(msg);

            _messageQueue.Enqueue(encodedMsg);
            _ = ProcessQueueAsync(); // Start processing the queue asynchronously
        }

        /// <summary>
        /// Processes the message queue asynchronously to ensure messages are sent one at a time.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            await _queueLock.WaitAsync();
            try
            {
                while (_messageQueue.TryDequeue(out var message))
                {
                    foreach (var itClient in ConnectedClients)
                    {
                        if (itClient == null || !itClient.IsConnected) continue;
                        itClient.SendMessage(message);
                    }
                }
            }
            finally
            {
                _queueLock.Release();
            }
        }

        #region IExchange

        public void Send(string data)
        {
            SendMessage(data, false);
        }

        #endregion
    }
}
