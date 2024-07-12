// Copyright (c) 2021 Dr. Christian Benjamin Ries
// Licensed under the MIT License
// File: QueryObjects.cs

namespace EsuEcosMiddleman.ECoS.Commands
{
    public class QueryObjects : Command
    {
        public static string N = "queryObjects";
        public override CommandT Type => CommandT.QueryObjects;
        public override string Name => N;
    }
}
