// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using Newtonsoft.Json;
using System.Net;

namespace EsuEcosMiddleman.Network
{
    public class CfgServer
    {
        [JsonIgnore]
        public IPAddress ListenIp { get; set; }

        [JsonProperty("bindingIp")]
        public string Ip
        {
            get => ListenIp.ToString();
            set => ListenIp = IPAddress.Parse(value);
        }

        [JsonProperty("listenPort")]
        public ushort ListenPort { get; set; } = 15471;
    }
}
