﻿// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System.Collections.Generic;

namespace EsuEcosMiddleman.ECoS
{
    public interface ICommandArgument
    {
        string Name { get; set; }
        List<string> Parameter { get; set; }
    }
}
