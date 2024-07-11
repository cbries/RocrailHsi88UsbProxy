// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.IO;
using log4net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace EsuEcosMiddleman
{
    internal class Program
    {
        public class Logger : ILogger
        {
            public ILog Log => LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        }

        public static CfgRuntime Cfg { get; set; }

        private static async Task Main()
        {
            var loggerInstance = new Logger();

            var cfgCnt = File.ReadAllText("EsuEcosMiddleman.json", Encoding.UTF8);
            Cfg = JsonConvert.DeserializeObject<CfgRuntime>(cfgCnt);
            Cfg.Logger = loggerInstance;

            var middleman = Middleman.Instance(Cfg);
            await middleman.RunAsync();

            if(Cfg.RuntimeConfiguration.IsSimulation)
                loggerInstance.Log?.Info("Simulation mode is activated!");
            loggerInstance.Log?.Info($"Started {DateTime.Now:F}");

            Console.WriteLine("Enter any key to quit...");
            Console.ReadKey();
        }
    }
}
