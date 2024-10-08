﻿// Copyright (c) 2021 Dr. Christian Benjamin Ries
// Licensed under the MIT License
// File: ConnectorFaster.cs

using System;
using System.Threading;
using System.Threading.Tasks;
using ThreadState = System.Threading.ThreadState;
// ReSharper disable AsyncVoidLambda

// ReSharper disable UnusedMember.Global

namespace EsuEcosMiddleman.Network
{
    public delegate void ConnectorFasterMessageReceived(object sender, MessageEventArgs eventArgs);
    public delegate void ConnectorFasterFailed(object sender, MessageEventArgs eventArgs);

    public class ConnectorFaster : IConnector, IExchange
    {
        public event EventHandler Started;
        public event ConnectorFasterFailed Failed;
        public event EventHandler Stopped;
        public event ConnectorFasterMessageReceived MessageReceived;

        #region IExchange

        public void Send(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            if (_client == null) return;
            if (!_client.IsConnected) return;

            (_client as IExchange)?.Send(data);
        }

        #endregion

        public ILogger Logger { get; set; }
        public string LastError { get; private set; }

        public string IpAddress { get; set; }
        public UInt16 Port { get; set; }

        #region IConnector

        public bool Start()
        {
            try
            {
                if (_thread is { IsAlive: true })
                    return true;

                _thread = new Thread(async () =>
                {
                    Thread.CurrentThread.IsBackground = true;

                    await StartHandler();
                });

                _thread.Start();

                _run = _thread.ThreadState == ThreadState.Running 
                       || _thread.ThreadState == ThreadState.Background;

                if (_run)
                    return true;

                try
                {
                    //_thread?.Abort(null);
                    _thread = null;
                }
                catch
                {
                    // ignore
                }

                Stopped?.Invoke(this, null!);

                return false;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool Stop()
        {
            if (!_run) return true;

            _run = false;
            
            try
            {
                _client?.Disconnect();
            }
            catch
            {
                // ignore
            }
            _client = null;

            if(_invokeStopEvent)
                Stopped?.Invoke(this, null!);

            _invokeStopEvent = true;

            return true;
        }

        #endregion

        private bool _run;
        private Thread _thread;
        private TcpClient2 _client;

        private async Task StartHandler()
        {
            var ipaddr = IpAddress;
            int port = Port;

            try
            {
                _client = new TcpClient2
                {
                    Logger = Logger,
                    ThreadInstance = _thread
                };

                _client.Failed += ClientOnFailed;

                _client.LineReceived += (_, line) =>
                {
                    if (string.IsNullOrEmpty(line)) return;
                    Logger?.Log?.Debug($"<Connector> Recv ({line.Length}): {line}");
                    MessageReceived?.Invoke(this, new MessageEventArgs(line));
                };
                _client.SendFailed += (_, ex) =>
                {
                    Logger?.Log?.Error($"<Connector> Send failed: {ex.Message}");
                    Failed?.Invoke(this, new MessageEventArgs($"Send of message failed", ex));
                };

                _client.Connect(ipaddr, port);

                Logger?.Log?.Info($"<Connector> Connection established to {ipaddr}:{port}");
                Started?.Invoke(this, null!);

                await _client.HandleLines();
            }
            catch (Exception ex)
            {
                _invokeStopEvent = false;
                Logger?.Log?.Error($"<Connector> Connection failed to {ipaddr}:{port} with {ex.Message}");
                Failed?.Invoke(this, new MessageEventArgs($"Connection failed to {ipaddr}:{port} with {ex.Message}", ex));
            }

            Stop();
        }

        private void ClientOnFailed(object sender, EventArgs e)
        {
            Logger?.Log?.Error($"<Connector> Connection closed unexpected!");
            Failed?.Invoke(this, new MessageEventArgs($"Connection closed unexpected"));
        }

        private bool _invokeStopEvent = true;
    }
}
