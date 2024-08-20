using System;
using System.Collections.Generic;
using WatsonWebsocket;

namespace EsuEcosMiddleman.Network
{
    internal class TcpWsServer : IExchange
    {
        private WatsonWsServer _serverInstance;
        private readonly List<ClientMetadata> _clients = new();

        public void Start(int port)
        {
            var hostAddr = $"http://localhost:{port}/s88";
            _serverInstance = new WatsonWsServer("localhost", port);
            _serverInstance.ClientConnected += ServerInstanceOnClientConnected;
            _serverInstance.ClientDisconnected += ServerInstanceOnClientDisconnected;
            _serverInstance.Start();
        }

        private void ServerInstanceOnClientConnected(object sender, ConnectionEventArgs e)
        {
            var client = e.Client;

            Console.WriteLine($"New connection: {client.IpPort}  Guid: {client.Guid}");

            _clients.Add(client);
        }

        private void ServerInstanceOnClientDisconnected(object sender, DisconnectionEventArgs e)
        {
            Console.WriteLine($"Disconnected: {e.Client.Guid}");
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
