// Copyright (c) 2021 Dr. Christian Benjamin Ries
// Licensed under the MIT License
// File: IConnector.cs

using System;

namespace EsuEcosMiddleman.Network
{
    public interface IConnector
    {
        ILogger Logger { get; set; }
        string IpAddress { get; set; }
        UInt16 Port { get; set; }

        bool Start();
        bool Stop();
    }
}
