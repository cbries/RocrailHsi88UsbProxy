using System;
using System.CodeDom;
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

        public const int NumberOfPins = 16;

        private string _nativeHexData = "0000";

        public string NativeHexData
        {
            get => _nativeHexData;
            private set => _nativeHexData = value.Replace(" ", string.Empty);
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
            //if (recentBinary.Equals(updateBinary, StringComparison.OrdinalIgnoreCase))
            //{
            //    for (var i = 0; i < NumberOfPins; ++i)
            //        _states[i] = DateTime.Now;
            //    return false;
            //}

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
                if (cOld == cNew) continue;

                if (cNew == '1')
                {
                    var isOnValid = (DateTime.Now - _states[i]).TotalMilliseconds > _cfgDebounce.On;
                    if (!isOnValid) continue;
                    
                    res = true;
                    sbin[i] = cNew;
                    _states[i] = DateTime.Now;
                }
                else if (cNew == '0')
                {
                    var isOffValid = (DateTime.Now - _states[i]).TotalMilliseconds > _cfgDebounce.Off;
                    if (!isOffValid) continue;

                    res = true;
                    sbin[i] = cNew;
                    _states[i] = DateTime.Now;
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
        internal string ToBinary(string hexValue)
        {
            return string.Join(string.Empty,
                hexValue.Select(
                    c => Convert.ToString(Convert.ToInt32(c.ToString(), 16), 2).PadLeft(4, '0')
                ));
        }

        internal string ToBinary2(string hexValue)
        {
            var m = ToBinary(hexValue);
            var parts = Split(m, 2);
            return string.Join(" ", parts);
        }

        private static IEnumerable<string> Split(string str, int chunkSize)
        {
            return Enumerable.Range(0, str.Length / chunkSize)
                .Select(i => str.Substring(i * chunkSize, chunkSize));
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