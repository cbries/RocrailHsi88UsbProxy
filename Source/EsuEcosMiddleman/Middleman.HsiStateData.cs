// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.Collections.Generic;
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
        public const int ObjectIdOffset = 100; // offset of "100" because ecos starts the object id with 100 for s88 modules
        public int ObjectId { get; } // ESU ECoS internal identifier
        public const int NumberOfPins = 16;
        public const string ZeroHexValues = "0000";
        public const char Bin1 = '1';
        public const char Bin0 = '0';

        private string _nativeHexData = ZeroHexValues;

        public string NativeHexData
        {
            get => _nativeHexData;
            private set => _nativeHexData = value?.Trim() ?? ZeroHexValues;
        }

        public string NativeBinaryData => ToBinary(NativeHexData);

        private readonly Dictionary<int, DateTime> _states = new();
        private ushort _nativeStateMask;

        public HsiStateData(int objectId, ICfgDebounce cfgDebounce)
        {
            ObjectId = objectId;

            _cfgDebounce = cfgDebounce;
            _nativeStateMask = 0;

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

            var updateMask = ParseHexMask(dataset);
            var workingMask = _nativeStateMask;
            var now = DateTime.Now;
            var changeCounter = 0;

            for (var i = 0; i < NumberOfPins; ++i)
            {
                var bitIndex = NumberOfPins - 1 - i;
                var cOld = ((workingMask >> bitIndex) & 1) == 1;
                var cNew = ((updateMask >> bitIndex) & 1) == 1;

                if (cOld == cNew)
                {
                    _states[i] = now;
                    continue;
                }

                var stateTime = _states[i];
                var deltaMs = (now - stateTime).TotalMilliseconds;

                var bounceOn = _cfgDebounce.On;
                var bounceOff = _cfgDebounce.Off;

                if (!cOld) // was off, must wait bounceOff before update
                {
                    if (deltaMs > bounceOn)
                    {
                        workingMask |= (ushort)(1 << bitIndex);
                        _states[i] = now;
                        ++changeCounter;
                    }
                }
                else // was on, must wait bounceOn beforeUpdate
                {
                    if (deltaMs > bounceOff)
                    {
                        workingMask &= (ushort)~(1 << bitIndex);
                        _states[i] = now;
                        ++changeCounter;
                    }
                }
            }

            if (changeCounter > 0)
            {
                _nativeStateMask = workingMask;
                NativeHexData = ToHexMask(workingMask);
            }

            return changeCounter > 0;
        }

#if DEBUG
        public void ShowStates(uint portId, int specificPin = -1, bool showHeadline = true, Action<string> logCallback = null)
        {
            var recentBinary = ToBinary(NativeHexData);

            if (showHeadline)
                logCallback?.Invoke("ShowStates");

            if (specificPin == -1)
            {
                for (var i = 0; i < NumberOfPins; ++i)
                {
                    var dt = _states[i];
                    var delta = DateTime.Now - dt;
                    logCallback?.Invoke($"   {recentBinary[i]}  {dt}  {delta.TotalMilliseconds}");
                }
            }
            else
            {
                var pinRight = NumberOfPins - specificPin;
                var dt = _states[pinRight];
                var delta = DateTime.Now - dt;
                logCallback?.Invoke($"{portId}:{specificPin}   {recentBinary[pinRight]}  {dt}  {delta.TotalMilliseconds}");
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
        private static ushort ParseHexMask(string hexValue)
        {
            if (string.IsNullOrWhiteSpace(hexValue)) return 0;
            var normalized = hexValue.Trim();
            while (normalized.Length < 4)
                normalized = "0" + normalized;
            normalized = normalized.Length > 4 ? normalized.Substring(normalized.Length - 4) : normalized;
            return Convert.ToUInt16(normalized, 16);
        }

        private static string ToHexMask(ushort mask)
        {
            return mask.ToString("X4");
        }

        public static string ToBinary(string hexValue)
        {
            return Convert.ToString(ParseHexMask(hexValue), 2).PadLeft(NumberOfPins, Bin0);
        }

        /// <summary>
        /// Example:
        ///     11111111 => FF
        ///     00001111 => 0F
        /// </summary>
        /// <param name="binaryValue"></param>
        /// <returns></returns>
        public static string ToHex(string binaryValue)
        {
            var normalized = binaryValue ?? string.Empty;
            while (normalized.Length < NumberOfPins)
                normalized = Bin0 + normalized;
            normalized = normalized.Length > NumberOfPins ? normalized.Substring(normalized.Length - NumberOfPins) : normalized;
            return Convert.ToUInt16(normalized, 2).ToString("X4");
        }
    }
}