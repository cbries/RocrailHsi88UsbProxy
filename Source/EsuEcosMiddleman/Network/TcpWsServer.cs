// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.Collections.Generic;
using WatsonWebsocket;

namespace EsuEcosMiddleman.Network
{
    internal class TcpWsServer : IExchange
    {
        private WatsonWsServer _serverInstance;
        private readonly List<ClientMetadata> _clients = new();

        public ILogger Logger { get; set; }

        public void Start(int port)
        {
            _serverInstance = new WatsonWsServer("+", port);
            _serverInstance.ClientConnected += ServerInstanceOnClientConnected;
            _serverInstance.ClientDisconnected += ServerInstanceOnClientDisconnected;
            _serverInstance.Start();

            Logger?.Log?.Info($"<TcpWsServer> Listen started... (Port: {port})");
        }

        private void ServerInstanceOnClientConnected(object sender, ConnectionEventArgs e)
        {
            var client = e.Client;

            Logger?.Log?.Info($"New connection: {client.IpPort}  Guid: {client.Guid}");

            _clients.Add(client);
        }

        private void ServerInstanceOnClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            Logger?.Log?.Info($"Disconnected: {e.Client.Guid}");
        }

        public async void Send(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            foreach (var it in _clients)
            {
                try
                {
                    await _serverInstance.SendAsync(it.Guid, data);
                }
                catch (Exception ex)
                {
                    ex.ShowException();
                }
            }
        }
    }
}
