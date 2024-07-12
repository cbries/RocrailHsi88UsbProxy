﻿// Copyright (c) 2021 Dr. Christian Benjamin Ries
// Licensed under the MIT License
// File: Unknown.cs

namespace EsuEcosMiddleman.ECoS.Commands
{
    public class Unknown : Command
    {
        public static string N = "Unknown";
        public override CommandT Type => CommandT.Unknown;
        public override string Name => N;
    }
}
