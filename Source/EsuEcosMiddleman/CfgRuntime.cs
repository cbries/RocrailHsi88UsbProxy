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
        IRuntimeConfiguration RuntimeConfiguration { get; set; }
        ICfgFilter Filter { get; set; }
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
