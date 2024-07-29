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

// ReSharper disable RedundantDefaultMemberInitializer

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
                _hsiStates[100 + idx] = new HsiStateData(_cfgRuntime.CfgDebounce);

            _hsi88Device = new DeviceInterface();
            _hsi88Device.Init(_cfgRuntime.CfgHsi88, _cfgRuntime);
            _hsi88Device.Failed += Hsi88DeviceOnFailed;
            _hsi88Device.Opened += Hsi88DeviceOnOpened;
            _hsi88Device.DataReceived += Hsi88DeviceOnDataReceived;

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
                _handler.SendToRocrail(GetStateOfModule2(objId));
            }
        }

        /// <summary>
        /// Describes the state of a single S88-device, i.e. 16 pins.
        /// </summary>
        public class HsiStateData
        {
            private readonly ICfgDebounce _cfgDebounce;

            public const int NumberOfPins = 16;
            public string NativeHexData { get; private set; } = "0000";

            private readonly Dictionary<int, DateTime> _states = new();

            public HsiStateData(ICfgDebounce cfgDebounce)
            {
                _cfgDebounce = cfgDebounce;

                for (var i = 0; i < NumberOfPins; ++i)
                    _states.Add(i, DateTime.MinValue);
            }

            /// <summary>
            /// Updates the internal information about a S88-device and its pin states.
            /// When no update is applied the method returns `false`, in any other
            /// cases `true` is returned.
            /// This method provides so-called "Entprellung" to avoid undisired
            /// internal updates when the track/s88 feedback is dirty and flickers the signal.
            /// </summary>
            /// <param name="dataset"></param>
            /// <returns></returns>
            public bool Update(string dataset)
            {
                if (string.IsNullOrEmpty(dataset)) return false;

                var recentBinary = ToBinary(NativeHexData);
                var updateBinary = ToBinary(dataset);
                if (recentBinary.Equals(updateBinary, StringComparison.OrdinalIgnoreCase))
                {
                    for(var i = 0; i < NumberOfPins; ++i)
                        _states[i] = DateTime.Now;

                    return false;
                }

                char[] sbin = new[]
                {
                    '0', '0', '0', '0',
                    '0', '0', '0', '0',
                    '0', '0', '0', '0',
                    '0', '0', '0', '0'
                };

                var res = false;

                for (var i = 0; i < NumberOfPins; ++i)
                {
                    sbin[i] = recentBinary[i];

                    var cOld = recentBinary[i];
                    var cNew = updateBinary[i];
                    if (cOld == cNew) continue;

                    if (cNew == '1')
                    {
                        var isOnValid = (DateTime.Now - _states[i]).TotalMilliseconds > _cfgDebounce.On;
                        _states[i] = DateTime.Now;
                        if (!isOnValid) continue;
                        
                        res = true;
                        sbin[i] = cNew;
                    }
                    else if (cNew == '0')
                    {
                        var isOffValid = (DateTime.Now - _states[i]).TotalMilliseconds > _cfgDebounce.Off;
                        _states[i] = DateTime.Now;
                        if (!isOffValid) continue;
                        
                        res = true;
                        sbin[i] = cNew;
                    }
                }

                NativeHexData = ToHex(new string(sbin));

                return res;
            }

            /// <summary>
            /// Example:
            ///     ff => 11111111
            ///     f0 => 11110000
            /// </summary>
            /// <param name="hexValue"></param>
            /// <returns></returns>
            private string ToBinary(string hexValue)
            {
                return string.Join(string.Empty,
                    hexValue.Select(
                        c => Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0')
                    ));
            }

            /// <summary>
            /// Example:
            ///     11111111 => FF
            ///     00001111 => 0F
            /// </summary>
            /// <param name="binaryValue"></param>
            /// <returns></returns>
            private string ToHex(string binaryValue)
            {
                var hex = string.Join(" ",
                    Enumerable.Range(0, binaryValue.Length / 8)
                        .Select(i => Convert.ToByte(binaryValue.Substring(i * 8, 8), 2).ToString("X2")));
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    hex = hex.Substring(2).Trim();
                return hex;
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
                // HSI-88-USB sends "1"-based port ids
                // Finally: 99 + 1       => 100 (objId)
                var objId = 99 + it.Key;

                var currentState = _hsiStates[objId].NativeHexData;
                var changed = !currentState.Equals(it.Value, StringComparison.OrdinalIgnoreCase);
                if (changed) continue;

                _hsiStates[objId].Update(it.Value);
                //_hsiStates[objId].NativeHexData = it.Value;

                _handler.SendToRocrail(GetStateOfModule2(objId));
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