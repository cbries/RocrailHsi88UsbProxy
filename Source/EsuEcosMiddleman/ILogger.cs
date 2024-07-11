// Copyright (c) 2021 Dr. Christian Benjamin Ries
// Licensed under the MIT License
// File: ILogger.cs

namespace EsuEcosMiddleman
{
    public interface ILogger
    {
        log4net.ILog Log { get; }
    }
}
