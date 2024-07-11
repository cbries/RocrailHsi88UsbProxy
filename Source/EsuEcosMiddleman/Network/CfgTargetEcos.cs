// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using Newtonsoft.Json;
using System.Net;

namespace EsuEcosMiddleman.Network
{
    public class CfgTargetEcos
    {
        [JsonIgnore]
        public IPAddress TargetIp { get; set; }

        [JsonProperty("ip")]
        public string Ip
        {
            get => TargetIp.ToString();
            set => TargetIp = IPAddress.Parse(value);
        }

        [JsonProperty("port")]
        public ushort TargetPort { get; set; } = 15471;
    }
}
