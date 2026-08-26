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

    internal interface ICfgS88Sync
    {
        uint FullStateBroadcastIntervalMs { get; set; }
    }

    public class CfgS88Sync : ICfgS88Sync
    {
        [JsonProperty("fullStateBroadcastIntervalMs")]
        public uint FullStateBroadcastIntervalMs { get; set; } = 2500;
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
        bool IsSimulation { get; set; }
    }

    public class RuntimeConfiguration : IRuntimeConfiguration
    {
        [JsonProperty("isSimulation")] public bool IsSimulation { get; set; } = false;
    }

    public class CfgMqtt
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("host")] public string Host { get; set; } = "localhost";
        [JsonProperty("port")] public int Port { get; set; } = 1883;
        [JsonProperty("clientId")] public string ClientId { get; set; } = "EsuEcosMiddleman";
        [JsonProperty("username")] public string Username { get; set; } = string.Empty;
        [JsonProperty("password")] public string Password { get; set; } = string.Empty;
    }

    public class CfgActionTrigger
    {
        [JsonProperty("address")] public int Address { get; set; } = -1;
        [JsonProperty("port")] public int? Port { get; set; }
        [JsonProperty("state")] public string State { get; set; }
    }

    public class CfgActionDefinition
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("topic")] public string Topic { get; set; }
        [JsonProperty("payload")] public object Payload { get; set; }
    }

    public class CfgLightAutomationPhase
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("atPercent")] public uint AtPercent { get; set; }
        [JsonProperty("action")] public CfgActionDefinition Action { get; set; } = new CfgActionDefinition();
    }

    public class CfgLightAutomation
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = false;
        [JsonProperty("switchAddress")] public int SwitchAddress { get; set; } = -1;
        [JsonProperty("dayDurationMinutes")] public uint DayDurationMinutes { get; set; } = 15;
        [JsonProperty("cyclePauseSeconds")] public uint CyclePauseSeconds { get; set; } = 30;
        [JsonProperty("manualOverrideAddresses")] public List<int> ManualOverrideAddresses { get; set; } = new List<int>();
        [JsonProperty("stopAction")] public CfgActionDefinition StopAction { get; set; } = new CfgActionDefinition();
        [JsonProperty("phases")] public List<CfgLightAutomationPhase> Phases { get; set; } = new List<CfgLightAutomationPhase>();
    }

    public class CfgActionMapping
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("trigger")] public CfgActionTrigger Trigger { get; set; } = new CfgActionTrigger();
        [JsonProperty("action")] public CfgActionDefinition Action { get; set; } = new CfgActionDefinition();
    }

    public class CfgActions
    {
        [JsonProperty("enabled")] public bool Enabled { get; set; } = true;
        [JsonProperty("mqtt")] public CfgMqtt Mqtt { get; set; } = new CfgMqtt();
        [JsonProperty("lightAutomation")] public CfgLightAutomation LightAutomation { get; set; } = new CfgLightAutomation();
        [JsonProperty("mappings")] public List<CfgActionMapping> Mappings { get; set; } = new List<CfgActionMapping>();
    }

    public class CfgExtensions
    {
        [JsonProperty("actions")] public CfgActions Actions { get; set; } = new CfgActions();
    }

    internal interface ICfgRuntime
    {
        ILogger Logger { get; set; }
        CfgServer CfgServer { get; set; }
        CfgTargetEcos CfgTargetEcos { get; set; }
        ICfgHsi88 CfgHsi88 { get; set; }
        ICfgS88Sync CfgS88Sync { get; set; }
        ICfgDebounce CfgDebounce { get; set; }
        IRuntimeConfiguration RuntimeConfiguration { get; set; }
        ICfgDebug DebugConfiguration { get; set; }
        ICfgFilter Filter { get; set; }
        CfgExtensions Extensions { get; set; }
    }

    internal class CfgRuntime : ICfgRuntime
    {
        [JsonIgnore]
        public ILogger Logger { get; set; }

        [JsonProperty("server")] public CfgServer CfgServer { get; set; } = new();
        [JsonProperty("ecos")] public CfgTargetEcos CfgTargetEcos { get; set; } = new();
        [JsonProperty("hsi")] public ICfgHsi88 CfgHsi88 { get; set; } = new CfgHsi88();
        [JsonProperty("s88Sync")] public ICfgS88Sync CfgS88Sync { get; set; } = new CfgS88Sync();
        [JsonProperty("debounce")] public ICfgDebounce CfgDebounce { get; set; } = new CfgDebounce();
        [JsonProperty("runtime")] public IRuntimeConfiguration RuntimeConfiguration { get; set; } = new RuntimeConfiguration();
        [JsonProperty("debug")] public ICfgDebug DebugConfiguration { get; set; } = new CfgDebug();
        [JsonProperty("filter")] public ICfgFilter Filter { get; set; } = new CfgFilter();
        [JsonProperty("extensions")] public CfgExtensions Extensions { get; set; } = new CfgExtensions();
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
