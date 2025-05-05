// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using EsuEcosMiddleman.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EsuEcosMiddleman.ECoS;
using EsuEcosMiddleman.HSI88USB;
using System.Net.NetworkInformation;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ReSharper disable RedundantDefaultMemberInitializer

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

namespace EsuEcosMiddleman
{
    internal partial class Middleman
    {
        private const ushort ObjectIdS88 = 26;
        private const string CommandLineTermination = "\r\n";

        private static Middleman _instance;
        private ICfgRuntime _cfgRuntime;
        private TcpServer _tcpServerInstance;
        private TcpWsServer _tcpWsServerInstance;
        private ConnectorFaster _tcpEcosClient;
        private DeviceInterface _hsi88Device;
        private MiddlemanHandler _handler;

        private bool IsSimulation => _cfgRuntime.RuntimeConfiguration.IsSimulation;

        public void Stop()
        {
            _isStopped = true;
        }

        public static Middleman Instance(ICfgRuntime cfgRuntime)
        {
            if (_instance != null) return _instance;
            _instance = new Middleman { _cfgRuntime = cfgRuntime };
            return _instance;
        }

        private void InitEcos()
        {
            if (IsSimulation) return;

            _tcpEcosClient = new ConnectorFaster
            {
                IpAddress = _cfgRuntime.CfgTargetEcos.TargetIp.ToString(),
                Logger = _cfgRuntime.Logger,
                Port = _cfgRuntime.CfgTargetEcos.TargetPort
            };

            _tcpEcosClient.MessageReceived += TcpEcosClientOnMessageReceived;
            _tcpEcosClient.Started += TcpEcosClientOnStarted;
            _tcpEcosClient.Failed += TcpEcosClientOnFailed;

            Task.Run(async () =>
            {
                await CheckReconnect();
            });
        }

        private void InitWsServer()
        {
            _tcpWsServerInstance = new TcpWsServer
            {
                Logger = _cfgRuntime.Logger
            };
            _tcpWsServerInstance.Start(_cfgRuntime.CfgServer.ListenPort + 1);
        }

        public async Task RunAsync()
        {
            if (_tcpServerInstance != null) return;
            if (_cfgRuntime == null)
                throw new Exception("No runtime configuration set.");

            _tcpServerInstance = new TcpServer(_cfgRuntime.CfgServer)
            {
                Logger = _cfgRuntime.Logger
            };
            _tcpServerInstance.ClientConnected += ServerInstanceOnClientConnected;
            _tcpServerInstance.ClientDisconnected += TcpServerInstanceOnClientDisconnected;
            _tcpServerInstance.MessageReceived += TcpServerInstanceOnMessageReceived;
            _tcpServerInstance.ClientFailed += TcpServerInstanceOnClientFailed;
            _tcpServerInstance.Stopped += TcpServerInstanceOnStopped;
            _tcpServerInstance.Listen();

            InitEcos();
            InitWsServer();

            // init hsi state cache
            // offset of "100" because ecos starts the object id with 100 for s88 modules
            for (var idx = 0; idx < 32; ++idx)
            {
                var objectId = HsiStateData.ObjectIdOffset + idx;

                _hsiStates[objectId] = new HsiStateData(objectId, _cfgRuntime.CfgDebounce);
            }

            _hsi88Device = new DeviceInterface();
            _hsi88Device.Failed += Hsi88DeviceOnFailed;
            _hsi88Device.Opened += Hsi88DeviceOnOpened;
            _hsi88Device.DataReceived += Hsi88DeviceOnDataReceived;
            _hsi88Device.Init(_cfgRuntime.CfgHsi88, _cfgRuntime);

            if (IsSimulation)
                await _hsi88Device.RunSimulationAsync();
            else
                await _hsi88Device.RunAsync();

            _handler = new MiddlemanHandler(_tcpServerInstance, _tcpEcosClient, _tcpWsServerInstance);
        }

        private void TcpEcosClientOnFailed(object sender, MessageEventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Fatal($"Ecos client handling failed: {eventargs.Exception.GetExceptionMessages()}");

            Task.Run(async () =>
            {
                await CheckReconnect();
            });
        }

        private bool IsPingToEcosOk()
        {
            var pingSender = new Ping();
            var options = new PingOptions
            {
                DontFragment = true
            };
            const int timeout = 120;
            const string data = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var buffer = Encoding.ASCII.GetBytes(data);
            var reply = pingSender.Send(_cfgRuntime.CfgTargetEcos.Ip, timeout, buffer, options);

            return (reply is { Status: IPStatus.Success });
        }

        private bool _isStopped = false;

        private async Task CheckReconnect()
        {
            var ecosAddr = $"{_cfgRuntime.CfgTargetEcos.TargetIp}:{_cfgRuntime.CfgTargetEcos.TargetPort}";
            while (!_isStopped)
            {
                if (IsPingToEcosOk())
                {
                    _cfgRuntime.Logger?.Log.Info($"Ping ok, try to connect to {ecosAddr}...");
                    break;
                }
                _cfgRuntime.Logger?.Log.Info($"Try to ping again in 5 seconds to {ecosAddr}...");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            _isStopped = false;
            _tcpEcosClient.Start();
        }

        private void TcpEcosClientOnStarted(object sender, EventArgs e)
        {
            _tcpEcosClient.Send($"get(1, info){CommandLineTermination}");
            _tcpEcosClient.Send($"get(1, status){CommandLineTermination}");
        }

        private readonly StringBuilder _messageBuffer = new();

        private void TcpEcosClientOnMessageReceived(object sender, MessageEventArgs eventargs)
        {
            var line = eventargs.Message?.Trim();
            if (string.IsNullOrEmpty(line)) return;
            _messageBuffer.Append(line).Append(CommandLineTermination);
            if (line.StartsWith("<REPLY") || line.StartsWith("<EVENT"))
            {
                // Start of a new block
                _messageBuffer.Clear();
                _messageBuffer.Append(line).Append(CommandLineTermination);
            }
            else if (line.StartsWith("<END"))
            {
                // End of a block
                var completeMessage = _messageBuffer.ToString();
                _messageBuffer.Clear();
                _cfgRuntime.Logger?.Log?.Debug($"ECoS [in] --> Rocrail [out]: {completeMessage.Trim()}");
                _handler?.SendToRocrail(completeMessage);
            }
            else
            {
                if(line.StartsWith("<", StringComparison.OrdinalIgnoreCase))
                    _cfgRuntime.Logger?.Log?.Debug($"UNKNOWN: {line.Trim()}");
            }
        }

        #region HSI-88-USB

        private void Hsi88DeviceOnOpened(object sender, EventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Info("HSI-88-USB interface opened");
        }

        private void Hsi88DeviceOnFailed(object sender, EventArgs eventargs)
        {
            var evArgs = eventargs as DeviceInterfaceEventArgs;
            _cfgRuntime.Logger?.Log.Fatal($"HSI-88-USB failed: {evArgs?.Message ?? "reason unknown"}");
        }

        private void SendCurrentHsi88States()
        {
            for (var i = 0; i < _cfgRuntime.CfgHsi88.NumberMax; ++i)
            {
                var objId = 100 + i;

                _handler.SendToRocrail(GetStateOfModule(objId));
                _handler.SendToWs(GetStateOfModule(objId, true));
            }
        }

        private readonly ConcurrentDictionary<int, HsiStateData> _hsiStates = new ConcurrentDictionary<int, HsiStateData>();

        private bool _versionShown = false;

        private void Hsi88DeviceOnDataReceived(object sender, DeviceInterfaceData data)
        {
            //_cfgRuntime.Logger?.Log.Debug($"HSI-88: {data.Data}");

            if (!_versionShown && data.Data.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                _versionShown = true;

                _cfgRuntime.Logger?.Log.Info($"HSI-88: {data.Data}");

                return;
            }

            foreach (var it in data.States)
            {
                //_cfgRuntime.Logger?.Log.Debug($"{it.Key} => {it.Value}");

                // ESU ECoS feedback device object ids starts at "100"
                // HSI-88-USB sends "1"-based port ids => hsiDeviceId
                // Finally: 99 + 1       => 100 (objId)
                var hsiDeviceId = it.Key;
                var objId = 99 + hsiDeviceId;

                var hsiPort = _hsiStates[objId];
                var r = hsiPort.Update(it.Value);

                if (r)
                {
                    var stateToSend = GetStateOfModule(objId);
                    _cfgRuntime.Logger?.Log.Debug($"S88->Rocrail: {stateToSend}");
                    _handler.SendToRocrail(stateToSend);

                    var stateJsonToSend = GetStateOfModule(objId, true);
                    _handler.SendToWs(stateJsonToSend);
                }
            }
        }

        #endregion

        // filter for S88 commands
        // all other commands, queries, etc.
        // are forwarded directly to the ecos
        private void DoS88Handling(ICommand command)
        {
            if (command.Type == CommandT.Request)
            {
                _handler.SendToRocrail($"<REPLY request({ObjectIdS88}, view)>{CommandLineTermination}<END 0 (OK)>");

                return;
            }

            if (command.Type == CommandT.Get)
            {
                var objId = command.ObjectId;
                if (objId == -1) return;
                if (objId == ObjectIdS88) return; // Do we need this in future?

                if (command.Arguments.Count == 0)
                {
                    var sbGet = new StringBuilder();
                    sbGet.Append($"  {objId} objectclass[feedback-module]{CommandLineTermination}");
                    sbGet.Append($"  {objId} view[none]{CommandLineTermination}");
                    sbGet.Append($"  {objId} listview[none]{CommandLineTermination}");
                    sbGet.Append($"  {objId} ports[16]{CommandLineTermination}");
                    sbGet.Append($"  {objId} state[{GetStateOfModule(objId)}]{CommandLineTermination}");

                    _handler.SendToRocrail($"<REPLY get({objId}>){CommandLineTermination}{sbGet}{CommandLineTermination}<END 0 (OK)>");
                }
                else
                {
                    if (command.Arguments.Count >= 2)
                    {
                        var firstArgument = command.Arguments.First()?.Name;
                        var secondArgument = command.Arguments[1]?.Name;

                        if (string.IsNullOrEmpty(firstArgument) && string.IsNullOrEmpty(secondArgument))
                        {
                            _cfgRuntime.Logger.Log?.Info($"Invalid command received: {command.NativeCommand}");

                            return;
                        }

                        switch (secondArgument)
                        {
                            /*
                               get(100, state)
                               get(101, state)
                               get(102, state)
                               get(103, state)
                             */
                            case "state":
                                _handler.SendToRocrail(GetStateOfModule(objId));
                                _handler.SendToWs(GetStateOfModule(objId, true));
                                break;
                        }
                    }
                }
            }
            else if (command.Type == CommandT.QueryObjects)
            {
                var objId = command.ObjectId;
                if (objId == -1) return;

                foreach (var arg in command.Arguments)
                {
                    if (string.IsNullOrEmpty(arg?.Name)) continue;

                    if (arg.Name.Equals("ports", StringComparison.OrdinalIgnoreCase))
                    {
                        var cfgHsi88 = _cfgRuntime.CfgHsi88;
                        var totalNoModules = cfgHsi88.NumberLeft + cfgHsi88.NumberMiddle + cfgHsi88.NumberRight;
                        var reply = $"<REPLY queryObjects(26,ports)>{CommandLineTermination}";
                        for (var i = 0; i < totalNoModules; ++i)
                            reply += $"{100 + i} ports[16]{CommandLineTermination}";
                        reply += $"<END 0 (OK)>{CommandLineTermination}";
                        _handler.SendToRocrail(reply);
                    }
                }
            }
        }

        private string GetStateOfModule(int objectId, bool asJson = false)
        {
            if (asJson)
            {
                var jsonEvent = new JObject
                {
                    ["objectId"] = objectId,
                    ["port"] = objectId - 100 + 1,
                    ["state"] = new JObject
                    {
                        ["hex"] = _hsiStates[objectId].NativeHexData,
                        ["binary"] = _hsiStates[objectId].NativeBinaryData
                    } 
                };

                var json = new JObject
                {
                    ["event"] = jsonEvent,
                    ["info"] = new JObject
                    {
                        ["left"] = _cfgRuntime.CfgHsi88.NumberLeft,
                        ["middle"] = _cfgRuntime.CfgHsi88.NumberMiddle,
                        ["right"] = _cfgRuntime.CfgHsi88.NumberRight
                    }
                };

                return json.ToString(Formatting.None);
            }

            var stateLine = $"{objectId} state[0x{_hsiStates[objectId].NativeHexData}]";
            var m = $"<EVENT {objectId}>{CommandLineTermination}{stateLine}{CommandLineTermination}<END 0 (OK)>";
            return m;
        }

        #region Server / Rocrail

        private void TcpServerInstanceOnMessageReceived(object sender, MessageEventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Debug($"Rocrail [in]: {eventargs.Message}");

            var receivedCmd = CommandFactory.Create(eventargs.Message);
            if (receivedCmd == null) return;
            var objId = receivedCmd.ObjectId;

            //
            // We have to support the ESU ECoSDetector
            // Therefor, relevant commands must be 
            // forwarded to the ECoS, but can be 
            // locally handled as well, i.e. two world support.
            //
            if (objId == ObjectIdS88)
            {
                _cfgRuntime.Logger?.Log.Debug($"Ecos [out]: {eventargs.Message}");

                _handler.SendToEcos(eventargs.Message);
            }
            
            // 
            // The middleware for the HSI-USB-S88
            //
            if (objId == ObjectIdS88 || objId is >= 100 and <= 131)
            {
                DoS88Handling(receivedCmd);
                return;
            }

            if (_cfgRuntime.Filter.Enabled)
            {
                if (Filter.IsFiltered(_cfgRuntime.Filter, receivedCmd, _cfgRuntime.Logger))
                {
                    _cfgRuntime.Logger?.Log.Debug($"Filtered message: {eventargs.Message}");
                    return;
                }
            }

            _cfgRuntime.Logger?.Log.Debug($"Ecos [out]: {eventargs.Message}");

            _handler.SendToEcos(eventargs.Message);
        }

        private void TcpServerInstanceOnStopped(object sender, EventArgs e)
        {
            _cfgRuntime.Logger?.Log.Warn($"Server for handling Rocrail data stopped.");
        }

        private void TcpServerInstanceOnClientDisconnected(object sender, ITcpClient client)
        {
            StopTaskForS88Updates(client);
        }

        private void TcpServerInstanceOnClientFailed(object sender, MessageEventArgs eventargs)
        {
            StopTaskForS88Updates(sender as TcpClient);

            _cfgRuntime.Logger?.Log.Fatal($"Client failed: {eventargs.Message} ({eventargs.Exception.GetExceptionMessages()})");
        }

        private void ServerInstanceOnClientConnected(object sender, ITcpClient client)
        {
            _cfgRuntime.Logger?.Log.Info($"Client connected: {client.Ip}");

            StartTaskForS88Updates(client);
        }

        #endregion

        private readonly Dictionary<ITcpClient, CancellationTokenSource> _clientTasks = new();

        private void StartTaskForS88Updates(ITcpClient client)
        {
            if (client == null) return;

            var cts = new CancellationTokenSource();
            _clientTasks[client] = cts;

            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested && client.IsConnected)
                {
                    SendCurrentHsi88States();
                    await Task.Delay(2500, cts.Token);
                }
            }, cts.Token);
        }

        private void StopTaskForS88Updates(ITcpClient client)
        {
            if (client == null) return;

            if (_clientTasks.TryGetValue(client, out var cts))
            {
                cts.Cancel();
                _clientTasks.Remove(client);
            }
        }
    }
}