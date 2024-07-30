// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.IO;
using System.Reflection;
using log4net;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace EsuEcosMiddleman
{
    internal class Program
    {
        public class Logger : ILogger
        {
            public ILog Log => LogManager.GetLogger("EsuEcosMiddleman");
        }

        public static CfgRuntime Cfg { get; set; }

        private static async Task Main()
        {
#if DEBUG
            Console.WriteLine("Attach debugger and enter any key...");
            Console.ReadKey();
#endif

            var loggerInstance = new Logger();

            var cfgCnt = File.ReadAllText("EsuEcosMiddleman.json", Encoding.UTF8);
            Cfg = JsonConvert.DeserializeObject<CfgRuntime>(cfgCnt);
            Cfg.Logger = loggerInstance;

            var middleman = Middleman.Instance(Cfg);
            await middleman.RunAsync();

            loggerInstance.Log?.Info($"Started {DateTime.Now:F}");

#if DEBUG
            for (var i = 0; i < 100; ++i)
            {
                Console.WriteLine($"CommandCount({i} / 100): enter to simulate S88");
                var keyInfo = Console.ReadKey();
                if (keyInfo.KeyChar == 'q' || keyInfo.KeyChar == 'Q')
                {
                    middleman.Stop();
                    return;
                }

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    middleman.DoSimulation();
                }
                else if (keyInfo.Key == ConsoleKey.Spacebar)
                {
                    if(i % 2 == 0)
                        middleman.DoSimulation("i01020000");
                    else
                        middleman.DoSimulation("i01020001");
                }
            }
#else
            Console.WriteLine("Enter any key to quit...");
            Console.ReadKey();
#endif
            middleman.Stop();
        }
    }
}
