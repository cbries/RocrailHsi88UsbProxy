// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System.Collections.Generic;
using EsuEcosMiddleman.Network;
using Newtonsoft.Json;

namespace EsuEcosMiddleman
{
    internal interface ICfgHsi88
    {
        ushort NumberLeft { get; set; }
        ushort NumberMiddle { get; set; }
        ushort NumberRight { get; set; }
        int NumberMax { get; }
        string DevicePath { get; set; }
    }

    internal class CfgHsi88 : ICfgHsi88
    {
        [JsonProperty("left")]
        public ushort NumberLeft { get; set; } = 0;
        [JsonProperty("middle")]
        public ushort NumberMiddle { get; set; } = 0;
        [JsonProperty("right")]
        public ushort NumberRight { get; set; } = 0;
        [JsonIgnore]
        public int NumberMax => NumberLeft + NumberMiddle + NumberRight;
        [JsonProperty("devicePath")]
        public string DevicePath { get; set; } = @"\\.\HsiUsb1";
    }

    internal interface ICfgDebounce
    {
        uint CheckInterval { get; set; }
        uint On { get; set; }
        uint Off { get; set; }
    }

    public class CfgDebounce : ICfgDebounce
    {
        [JsonProperty("checkIntervalMs")] public uint CheckInterval { get; set; }
        [JsonProperty("onMs")] public uint On { get; set; }
        [JsonProperty("offMs")] public uint Off { get; set; }
    }

    internal interface ICfgDebug
    {
        bool Enabled { get; set; }

        Dictionary<string, List<int>> Inputs { get; set; }
    }

    public class CfgDebug : ICfgDebug
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = false;

        // key: "port1", "port2", ...
        // value: 1..16
        [JsonProperty("inputs")]
        public Dictionary<string, List<int>> Inputs { get; set; }

        /// <summary>
        /// Filters the number of a port for a specific input.
        /// </summary>
        /// <param name="inputName">e.g. `port1` or `port3`, depends on the naming in the configuration</param>
        /// <returns></returns>
        public static uint GetPortNumber(string inputName)
        {
            if (string.IsNullOrEmpty(inputName)) return 0;
            var m = inputName.Replace("port", string.Empty).Trim();
            if (uint.TryParse(m, out var res))
                return res;
            return 0;
        }
    }

    public interface IRuntimeConfiguration
    {
    }

    public class RuntimeConfiguration : IRuntimeConfiguration
    {

    }

    internal interface ICfgRuntime
    {
        ILogger Logger { get; set; }
        CfgServer CfgServer { get; set; }
        CfgTargetEcos CfgTargetEcos { get; set; }
        ICfgHsi88 CfgHsi88 { get; set; }
        ICfgDebounce CfgDebounce { get; set; }
        IRuntimeConfiguration RuntimeConfiguration { get; set; }
        ICfgDebug DebugConfiguration { get; set; }
        ICfgFilter Filter { get; set; }
    }

    internal class CfgRuntime : ICfgRuntime
    {
        [JsonIgnore]
        public ILogger Logger { get; set; }

        [JsonProperty("server")] public CfgServer CfgServer { get; set; } = new();
        [JsonProperty("ecos")] public CfgTargetEcos CfgTargetEcos { get; set; } = new();
        [JsonProperty("hsi")] public ICfgHsi88 CfgHsi88 { get; set; } = new CfgHsi88();
        [JsonProperty("debounce")] public ICfgDebounce CfgDebounce { get; set; } = new CfgDebounce();
        [JsonProperty("runtime")] public IRuntimeConfiguration RuntimeConfiguration { get; set; } = new RuntimeConfiguration();
        [JsonProperty("debug")] public ICfgDebug DebugConfiguration { get; set; } = new CfgDebug();
        [JsonProperty("filter")] public ICfgFilter Filter { get; set; } = new CfgFilter();
    }

    internal interface ICfgFilter
    {
        bool Enabled { get; set; }
        List<string> ObjectIdsInfo { get; set; }
        List<int> ObjectIds { get; set; }
        List<string> ObjectIdRanges { get; set; }
    }

    internal class CfgFilter : ICfgFilter
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("objectIdsInfo")]
        public List<string> ObjectIdsInfo { get; set; } = new();
        [JsonProperty("objectIds")]
        public List<int> ObjectIds { get; set; } = new();
        [JsonProperty("objectIdRanges")]
        public List<string> ObjectIdRanges { get; set; } = new();
    }
}
