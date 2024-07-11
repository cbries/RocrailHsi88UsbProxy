// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System.Collections.Concurrent;

namespace EsuEcosMiddleman.Network
{
    public interface ITcpServer
    {
        ILogger Logger { get; set; }

        ConcurrentBag<ITcpClient> ConnectedClients { get; }

        CfgServer GetCfg();
        void Listen();
        void Stop();
        void SendMessage(string msg, bool forceBase64Encode = true);
    }
}
