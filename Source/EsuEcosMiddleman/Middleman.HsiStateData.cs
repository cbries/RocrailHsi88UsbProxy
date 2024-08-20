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
        public int HsiDeviceId => ObjectId - ObjectIdOffset + 1;
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

        public HsiStateData(int objectId, ICfgDebounce cfgDebounce)
        {
            ObjectId = objectId;

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
                Bin0, Bin0, Bin0, Bin0,
                Bin0, Bin0, Bin0, Bin0,
                Bin0, Bin0, Bin0, Bin0,
                Bin0, Bin0, Bin0, Bin0
            };

            var changeCounter = 0;

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

                if (cOld == Bin0) // was off, must wait bounceOff before update
                {
                    if (deltaMs > bounceOn)
                    {
                        sbin[i] = Bin1;

                        ++changeCounter;
                    }
                }
                else if (cOld == Bin1) // was on, must wait bounceOn beforeUpdate
                {
                    if (deltaMs > bounceOff)
                    {
                        sbin[i] = Bin0;

                        ++changeCounter;
                    }
                }
            }

            NativeHexData = ToHex(new string(sbin));

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
        public static string ToBinary(string hexValue)
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
        public static string ToHex(string binaryValue)
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