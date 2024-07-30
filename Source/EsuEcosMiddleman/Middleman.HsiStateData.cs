﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace EsuEcosMiddleman;

internal partial class Middleman
{
#pragma warning disable CS4014
    /// <summary>
    /// Describes the state of a single S88-device, i.e. 16 pins.
    /// </summary>
    public class HsiStateData
    {
        private readonly ICfgDebounce _cfgDebounce;

        public const int NumberOfPins = 16;
        public const string ZeroHexValues = "0000";

        private string _nativeHexData = ZeroHexValues;

        public string NativeHexData
        {
            get => _nativeHexData;
            private set => _nativeHexData = value?.Trim() ?? ZeroHexValues;
        }

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

            var sbin = new[]
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
                if (cOld == cNew)
                {
                    _states[i] = DateTime.Now;

                    continue;
                }
                 
                var stateTime = _states[i];
                var deltaMs = (DateTime.Now - stateTime).TotalMilliseconds;

                var bounceOn = _cfgDebounce.On;
                var bounceOff = _cfgDebounce.Off;

                if (cOld == '0') // was off, must wait bounceOff before update
                {
                    res = deltaMs > bounceOn;

                    if (res)
                    {
                        sbin[i] = '1';
                    }
                }
                else if (cOld == '1') // was on, must wait bounceOn beforeUpdate
                {
                    res = deltaMs > bounceOff;

                    if (res)
                    {
                        sbin[i] = '0';
                    }
                }
            }

            NativeHexData = ToHex(new string(sbin));

            return res;
        }

#if DEBUG
        public void ShowStates(int specificPin = -1, bool showHeadline = true)
        {
            var recentBinary = ToBinary(NativeHexData);

            if(showHeadline)
                Trace.WriteLine("ShowStates");

            for (var i = 0; i < NumberOfPins; ++i)
            {
                if (specificPin == -1)
                {
                    var dt = _states[i];
                    var delta = DateTime.Now - dt;
                    Trace.WriteLine($"   {recentBinary[i]}  {dt}  {delta.TotalMilliseconds}");
                }
                else
                {
                    if (specificPin == i + 1)
                    {
                        var dt = _states[i];
                        var delta = DateTime.Now - dt;
                        Trace.WriteLine($"   {recentBinary[i]}  {dt}  {delta.TotalMilliseconds}");
                    }
                }
            }
        }
#endif

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
            var hex = string.Join(string.Empty,
                Enumerable.Range(0, binaryValue.Length / 8)
                    .Select(i => Convert.ToByte(binaryValue.Substring(i * 8, 8), 2).ToString("X2")));
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2).Trim();
            return hex;
        }
    }
}