﻿// Copyright (c) 2021 Dr. Christian Benjamin Ries
// Licensed under the MIT License
// File: TcpClient2.cs

using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace EsuEcosMiddleman.Network
{
    public class TcpClient2 : TcpClient, IExchange
    {
        public new int Port { get; set; }
        public string Host { get; set; }

        public void Connect(string hostname, int port)
        {
            Port = port;
            Host = hostname;

            try
            {
                NativeClient = new System.Net.Sockets.TcpClient(Host, Port);
            }
            catch (SocketException ex)
            {
                Logger?.Log?.Error("Connect failed", ex);
                throw new Exception(ex.Message);
            }
        }

        #region IExchange

        public void Send(string data)
        {
            base.SendMessage(data);
        }
        
        #endregion

    }
}
