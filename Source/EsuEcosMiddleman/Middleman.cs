// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using EsuEcosMiddleman.Network;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EsuEcosMiddleman.ECoS;
using EsuEcosMiddleman.HSI88USB;
using System.Net.NetworkInformation;

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
        private ConnectorFaster _tcpEcosClient;
        private DeviceInterface _hsi88Device;
        private MiddlemanHandler _handler;

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
            _tcpServerInstance.MessageReceived += TcpServerInstanceOnMessageReceived;
            _tcpServerInstance.ClientFailed += TcpServerInstanceOnClientFailed;
            _tcpServerInstance.Stopped += TcpServerInstanceOnStopped;
            _tcpServerInstance.Listen();

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

            await _hsi88Device.RunAsync();

            _handler = new MiddlemanHandler(_tcpServerInstance, _tcpEcosClient);
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

        private void TcpEcosClientOnMessageReceived(object sender, MessageEventArgs eventargs)
        {
            var line = eventargs.Message.Trim();

            _cfgRuntime.Logger?.Log.Debug($"ECoS [in] --> Rocrail [out]: {line}");

            _handler.SendToRocrail(line);
        }

        private void Hsi88DeviceOnOpened(object sender, EventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Info("HSI-88-USB interface opened");
        }

        private void Hsi88DeviceOnFailed(object sender, EventArgs eventargs)
        {
            var evArgs = eventargs as DeviceInterfaceEventArgs;
            _cfgRuntime.Logger?.Log.Fatal($"HSI-88-USB failed: {evArgs?.Message ?? "reason unknown"}");
        }

        #region HSI-88-USB

        private void SendCurrentHsi88States()
        {
            for (var i = 0; i < _cfgRuntime.CfgHsi88.NumberMax; ++i)
            {
                var objId = 100 + i;
                var state = GetStateOfModule2(objId);
                _handler.SendToRocrail(state);
            }
        }

        private readonly ConcurrentDictionary<int, HsiStateData> _hsiStates = new ConcurrentDictionary<int, HsiStateData>();

        private bool _versionShown = false;

        private void Hsi88DeviceOnDataReceived(object sender, DeviceInterfaceData data)
        {
            _cfgRuntime.Logger?.Log.Debug($"HSI-88: {data.Data}");

            if (!_versionShown && data.Data.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                _versionShown = true;

                _cfgRuntime.Logger?.Log.Info($"HSI-88: {data.Data}");

                return;
            }

            foreach (var it in data.States)
            {
                _cfgRuntime.Logger?.Log.Debug($"{it.Key} => {it.Value}");

                // ESU ECoS feedback device object ids starts at "100"
                // HSI-88-USB sends "1"-based port ids => hsiDeviceId
                // Finally: 99 + 1       => 100 (objId)
                var hsiDeviceId = it.Key;
                var objId = 99 + hsiDeviceId;

                var hsiPort = _hsiStates[objId];
                var r = hsiPort.Update(it.Value);

#if DEBUG
                if (r) ShowStates(hsiPort);
#endif

                if (r)
                {
                    var stateToSend = GetStateOfModule2(objId);
                    _cfgRuntime.Logger?.Log.Debug($"S88->Rocrail: {stateToSend}");
                    _handler.SendToRocrail(stateToSend);
                }
            }
        }

#if DEBUG
        private void ShowStates(HsiStateData hsiPort)
        {
            if (hsiPort == null) return;
            if (!_cfgRuntime.DebugConfiguration.Enabled) return;

            foreach (var input in _cfgRuntime.DebugConfiguration.Inputs)
            {
                var portId = CfgDebug.GetPortNumber(input.Key);
                if (portId == 0) continue;
                if (portId != hsiPort.HsiDeviceId) continue;
                foreach (var pin in input.Value)
                    hsiPort.ShowStates(portId, pin, false, (msg) =>
                    {
                        _cfgRuntime.Logger.Log.Info($"{msg}");
                    });
            }
        }
#endif

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

                if (command.Arguments.Count == 0)
                {
                    var sbGet = new StringBuilder();
                    sbGet.Append($"  {objId} objectclass[feedback-module]{CommandLineTermination}");
                    sbGet.Append($"  {objId} view[none]{CommandLineTermination}");
                    sbGet.Append($"  {objId} listview[none]{CommandLineTermination}");
                    sbGet.Append($"  {objId} ports[16]{CommandLineTermination}");
                    sbGet.Append($"  {objId} state[{GetStateOfModule2(objId)}]{CommandLineTermination}");

                    _handler.SendToRocrail($"<REPLY get({objId}>){CommandLineTermination}{sbGet}{CommandLineTermination}<END 0 (OK)>");
                }
                else
                {
                    var firstArgument = command.Arguments.First()?.Name;
                    if (string.IsNullOrEmpty(firstArgument)) return;

                    switch (firstArgument)
                    {
                        /*
                           get(100, state)
                           get(101, state)
                           get(102, state)
                           get(103, state)
                         */
                        case "state":
                            _handler.SendToRocrail(GetStateOfModule2(objId));
                            break;
                    }
                }
            }
            else if (command.Type == CommandT.QueryObjects)
            {
                var objId = command.ObjectId;
                if (objId == -1) return;

                var firstArgument = command.Arguments.First()?.Name;
                if (string.IsNullOrEmpty(firstArgument)) return;

                if (firstArgument.Equals("ports"))
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

        private string GetStateOfModule2(int objectId)
        {
            var stateLine = $"{objectId} state[0x{_hsiStates[objectId].NativeHexData}]";
            var m = $"<EVENT {objectId}>{CommandLineTermination}{stateLine}{CommandLineTermination}<END 0 (OK)>";
            //_cfgRuntime.Logger?.Log.Debug($"S88:\r\n{m}");
            return m;
        }

        #region Server / Rocrail

        private void TcpServerInstanceOnMessageReceived(object sender, MessageEventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Debug($"Rocrail [in]: {eventargs.Message}");

            var receivedCmd = CommandFactory.Create(eventargs.Message);
            if (receivedCmd == null) return;
            var objId = receivedCmd.ObjectId;
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

        private void TcpServerInstanceOnClientFailed(object sender, MessageEventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Fatal($"Client failed: {eventargs.Message} ({eventargs.Exception.GetExceptionMessages()})");
        }

        private void ServerInstanceOnClientConnected(object sender, ITcpClient client)
        {
            _cfgRuntime.Logger?.Log.Info($"Client connected: {client.Ip}");

            SendCurrentHsi88States();
        }

        #endregion
    }
}