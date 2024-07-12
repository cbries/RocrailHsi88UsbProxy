// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using System.Threading;
using System.IO.Pipes;

namespace EsuEcosMiddleman.HSI88USB
{
    public delegate void DeviceInterfaceOpened(object sender, EventArgs eventArgs);

    public delegate void DeviceInterfaceFailed(object sender, EventArgs eventArgs);

    public delegate void DeviceInterfaceDataReceived(object sender, DeviceInterfaceData data);

    public class DeviceInterfaceEventArgs(string message) : EventArgs
    {
        public string Message { get; } = message;
    }

    public class DeviceInterfaceData(string data)
    {
        public DateTime Dt { get; set; } = DateTime.Now;
        public string Data { get; set; } = data;

        /*
         * Sample data:
         * i01040506
         * i01022c05
         *
         *„i“ <Anzahl der Module, die gemeldet werden>
            <Modulnummer> <HighByte> <LowByte>
            <Modulnummer> <HighByte> <LowByte>
            <Modulnummer> <HighByte> <LowByte>
            <CR>

            -> i 01 02 2c05
        */

        public int NumberOfModules
        {
            get
            {
                if (string.IsNullOrEmpty(Data)) return 0;
                if (!Data.StartsWith("i")) return 0;
                var data = Data.TrimStart('i').Trim();
                var p0 = data.Substring(0, 2).Trim();
                if (int.TryParse(p0, out var v))
                    return v;
                return 0;
            }
        }

        /// <summary>
        /// key := module id
        /// value := module state
        /// </summary>
        public Dictionary<int, string> States
        {
            get
            {
                var res = new Dictionary<int, string>();
                var noOfModules = NumberOfModules;
                if (noOfModules == 0) return res;
                var p = Data.Substring("i00".Length);
                if (string.IsNullOrEmpty(p)) return res;
                for (var idx = 0; idx < p.Length; idx += 6)
                {
                    var part0 = p.Substring(idx, 6);
                    var pModuleId = part0.Substring(0, 2);
                    var pModuleState = part0.Substring(2);

                    if (int.TryParse(pModuleId, out var mid))
                    {
                        res[mid] = pModuleState;
                    }
                }

                return res;
            }
        }
    }

    internal class DeviceInterface
    {
        public event DeviceInterfaceOpened Opened;
        public event DeviceInterfaceFailed Failed;
        public event DeviceInterfaceDataReceived DataReceived;

        private SafeFileHandle _handle = null;
        private ICfgHsi88 _cfgHsi88 = null;
        private ICfgRuntime _cfgRuntime = null;

        private FileStream _fs;

        private CancellationTokenSource _cancellationToken;

        private void Open()
        {
            _handle = NativeMethods.CreateFile(
                _cfgHsi88.DevicePath,
                NativeMethods.GENERIC_READ | NativeMethods.GENERIC_WRITE,
                0,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (_handle.IsInvalid)
            {
                Failed?.Invoke(this, new DeviceInterfaceEventArgs("Failed to open device."));
                throw new Exception("Failed to open device.");
            }

            Opened?.Invoke(this, EventArgs.Empty);
        }

        public void Close()
        {
            _cancellationToken.Cancel(false);
        }

        public void Init(ICfgHsi88 cfgHsi88, ICfgRuntime cfgRuntime)
        {
            _cfgHsi88 = cfgHsi88;
            _cfgRuntime = cfgRuntime;

            try
            {
                Open();

                _cancellationToken = new CancellationTokenSource();
            }
            catch (Exception ex)
            {
                ex.ShowException();
            }
        }

        /// <summary>
        /// Examples:
        ///     t\r             terminal mode
        ///     m\r             current states
        ///     s010205\r       apply number of shift registers
        ///     v\r             query version
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public bool Send(string command)
        {
            if (_fs == null) return false;
            if (!_fs.CanWrite) return false;
            if (!command.EndsWith("\r")) return false;

            try
            {
                var bytes = Encoding.ASCII.GetBytes(command);
                _fs.Write(bytes, 0, command.Length);
                return true;
            }
            catch (Exception ex)
            {
                ex.ShowException();
            }

            return false;
        }

        public async Task RunAsync()
        {
            Task.Run(async () =>
            {
                var buffer = new byte[1024];
                var bytesRead = 0;

                _fs = new FileStream(_handle, FileAccess.ReadWrite, 4096, isAsync: false);

                // init terminal mode
                Send("t\r");
                bytesRead = _fs.Read(buffer, 0, buffer.Length);
                var d0 = new DeviceInterfaceData(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                DataReceived?.Invoke(this, d0);

                // init number of shift registers
                Send($"s{_cfgRuntime.CfgHsi88.NumberLeft:D2}{_cfgRuntime.CfgHsi88.NumberMiddle:D2}{_cfgRuntime.CfgHsi88.NumberRight:D2}\r");
                bytesRead = _fs.Read(buffer, 0, buffer.Length);
                var d1 = new DeviceInterfaceData(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                DataReceived?.Invoke(this, d1);

                //// query current state
                //Send("m\r");
                //bytesRead = _fs.Read(buffer, 0, buffer.Length);
                //var d2 = new DeviceInterfaceData(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                //DataReceived?.Invoke(this, d2);

                var tkn = _cancellationToken.Token;

                while (!tkn.IsCancellationRequested)
                {
                    if (_fs.CanRead)
                    {
                        bytesRead = await _fs.ReadAsync(buffer, 0, buffer.Length, tkn);

                        if (bytesRead > 0)
                        {
                            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            var lines = text.Split(new []{'\r'}, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                if (string.IsNullOrEmpty(line)) continue;

                                DataReceived?.Invoke(this, new DeviceInterfaceData(line.Trim()));
                            }
                        }
                    }

                    Thread.Sleep(50);
                }
            });
        }
    }
}
