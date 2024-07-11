// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License
using EsuEcosMiddleman.Network;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EsuEcosMiddleman.ECoS;
using EsuEcosMiddleman.HSI88USB;

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

namespace EsuEcosMiddleman
{
    internal class Middleman
    {
        private const ushort ObjectIdS88 = 26;
        private const string CommandLineTermination = "\r\n";

        private static Middleman _instance;
        private ICfgRuntime _cfgRuntime;
        private TcpServer _tcpServerInstance;
        private ConnectorFaster _tcpEcosClient;
        private DeviceInterface _hsi88Device;
        private MiddlemanHandler _handler;

        public static Middleman Instance(ICfgRuntime cfgRuntime)
        {
            if (_instance != null) return _instance;
            _instance = new Middleman { _cfgRuntime = cfgRuntime };
            return _instance;
        }

        public async Task RunAsync()
        {
            if (_tcpServerInstance != null) return;

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

            if (_cfgRuntime.RuntimeConfiguration.ConnectToEcos)
            {
                _tcpEcosClient.MessageReceived += TcpEcosClientOnMessageReceived;
                _tcpEcosClient.Started += TcpEcosClientOnStarted;
                _tcpEcosClient.Failed += TcpEcosClientOnFailed;
                var r = _tcpEcosClient.Start();

                if (!r)
                    _cfgRuntime.Logger?.Log.Fatal($"Ecos client handling failed.");
            }
            else
            {
                _cfgRuntime.Logger?.Log.Info($"Ecos connection is disabled by user.");
            }

            // init hsi state cache
            // offset of "100" because ecos starts the object id with 100 for s88 modules
            for (var idx = 0; idx < 32; ++idx)
                _hsiStates[100 + idx] = "0000";

            _hsi88Device = new DeviceInterface();
            _hsi88Device.Init(_cfgRuntime.CfgHsi88, _cfgRuntime);
            _hsi88Device.Failed += Hsi88DeviceOnFailed;
            _hsi88Device.Opened += Hsi88DeviceOnOpened;
            _hsi88Device.DataReceived += Hsi88DeviceOnDataReceived;

            //
            // simulate S88 module state
            // random states send to Rocrail
            //
            if (_cfgRuntime.RuntimeConfiguration.IsS88Simulation)
            {
                Task.Run(async () =>
                {
                    var walltime = DateTime.Now + TimeSpan.FromSeconds(60);

                    while (DateTime.Now < walltime)
                    {
                        Hsi88SimulateStates();

                        await Task.Delay(TimeSpan.FromSeconds(3));
                    }
                });
            }
            else
            {
                await _hsi88Device.RunAsync();
            }

            _handler = new MiddlemanHandler(_tcpServerInstance, _tcpEcosClient);
        }

        private void TcpEcosClientOnFailed(object sender, MessageEventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Fatal($"Ecos client handling failed: {eventargs.Exception.GetExceptionMessages()}");
        }

        private void TcpEcosClientOnStarted(object sender, EventArgs e)
        {
            // ignore
        }

        private void TcpEcosClientOnMessageReceived(object sender, MessageEventArgs eventargs)
        {
            var line = eventargs.Message.Trim();

            _cfgRuntime.Logger?.Log.Info($"ECoS [in] --> Rocrail [out]: {line}");

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

        private static readonly Random Rnd = new Random();

        private void Hsi88SimulateStates()
        {
            for (var i = 0; i < _cfgRuntime.CfgHsi88.NumberMax; ++i)
            {
                var objId = 100 + i;
                _hsiStates[objId] = Rnd.Next(0, 0xffff).ToString("X");
                _handler.SendToRocrail(GetStateOfModule2(objId));
            }
        }

        private readonly ConcurrentDictionary<int, string> _hsiStates = new ConcurrentDictionary<int, string>();

        private void Hsi88DeviceOnDataReceived(object sender, DeviceInterfaceData data)
        {
            _cfgRuntime.Logger?.Log.Info($"HSI-88: {data.Data}");

            foreach (var it in data.States)
            {
                _cfgRuntime.Logger?.Log.Info($"{it.Key} => {it.Value}");

                var objId = 99 + it.Key;
                _hsiStates[objId] = it.Value;

                _handler.SendToRocrail(GetStateOfModule2(objId));
            }
        }

        #endregion

        private readonly Dictionary<string, string> _simulationData = new Dictionary<string, string>();

        private void LoadSimulationData()
        {
            if (_simulationData.Count != 0) return;

            var files = Directory.GetFiles(@"ECoS\SimulationData\", "*.txt", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return;
            foreach (var it in files)
            {
                try
                {
                    var fname = Path.GetFileNameWithoutExtension(it);
                    var cnt = File.ReadAllText(it);
                    if (!_simulationData.ContainsKey(fname))
                        _simulationData[fname] = cnt;
                }
                catch
                {
                    // ignore
                }
            }
        }

        private string GenerateSimulationDataKey(ICommand command)
        {
            if (command == null) return string.Empty;
            if (command.Type == CommandT.Get)
                return $"Reply_" + string.Join("_", command.Arguments);
            // add more if needed
            return string.Empty;
        }

        private void DoRocrailSimulation(ICommand command)
        {
            LoadSimulationData();

            var simkey = GenerateSimulationDataKey(command);
            if (_simulationData.TryGetValue(simkey, out var simdata))
            {
                _handler.SendToRocrail(simdata);
            }
            else
            {
                if (command.ArgumentsHas("view"))
                {
                    _handler.SendToRocrail($"<REPLY request({command.ObjectId}, view)>{CommandLineTermination}<END 0 (OK)>");
                }
            }
        }

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
                        reply += $"{100+i} ports[16]{CommandLineTermination}";
                    reply += $"<END 0 (OK)>{CommandLineTermination}";
                    _handler.SendToRocrail(reply);
                }
            }
        }

        private string GetStateOfModule2(int objectId)
        {
            var stateLine = $"{objectId} state[0x{_hsiStates[objectId]}]";
            var m = $"<EVENT {objectId}>{CommandLineTermination}{stateLine}{CommandLineTermination}<END 0 (OK)>";
            //_cfgRuntime.Logger?.Log.Debug($"S88:\r\n{m}");
            return m;
        }


        #region Server / Rocrail

        private void TcpServerInstanceOnMessageReceived(object sender, MessageEventArgs eventargs)
        {
            _cfgRuntime.Logger?.Log.Info($"Rocrail [in]: {eventargs.Message}");

            if (_cfgRuntime.RuntimeConfiguration.IsSimulation)
            {
                var cmdSimulation = CommandFactory.Create(eventargs.Message);
                DoRocrailSimulation(cmdSimulation);
                return;
            }

            var receivedCmd = CommandFactory.Create(eventargs.Message);
            if (receivedCmd == null) return;
            var objId = receivedCmd.ObjectId;
            if (objId == ObjectIdS88 || objId is >= 100 and <= 131)
            {
                DoS88Handling(receivedCmd);
                return;
            }

            _cfgRuntime.Logger?.Log.Info($"Ecos [out]: {eventargs.Message}");
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
        }

        #endregion
    }
}