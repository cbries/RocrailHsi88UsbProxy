// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

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

    public interface IRuntimeConfiguration
    {
        bool IsSimulation { get; set; }
        bool IsS88Simulation { get; set; }
        bool ConnectToEcos { get; set; }
    }

    public class RuntimeConfiguration : IRuntimeConfiguration
    {
        [JsonProperty("isSimulation")]
        public bool IsSimulation { get; set; } = false;

        [JsonProperty("isS88Simulation")]
        public bool IsS88Simulation { get; set; } = false;

        [JsonProperty("connectToEcos")]
        public bool ConnectToEcos { get; set; } = true;
    }

    internal interface ICfgRuntime
    {
        ILogger Logger { get; set; }
        CfgServer CfgServer { get; set; }
        CfgTargetEcos CfgTargetEcos { get; set; }
        ICfgHsi88 CfgHsi88 { get; set; }
        IRuntimeConfiguration RuntimeConfiguration { get; set; }
    }

    internal class CfgRuntime : ICfgRuntime
    {
        [JsonIgnore]
        public ILogger Logger { get; set; }
        [JsonProperty("server")]
        public CfgServer CfgServer { get; set; } = new();
        [JsonProperty("ecos")]
        public CfgTargetEcos CfgTargetEcos { get; set; } = new();
        [JsonProperty("hsi")]
        public ICfgHsi88 CfgHsi88 { get; set; } = new CfgHsi88();
        [JsonProperty("runtime")]
        public IRuntimeConfiguration RuntimeConfiguration { get; set; } = new RuntimeConfiguration();
    }
}
