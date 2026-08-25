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

        private static string ResolveConfigPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "EsuEcosMiddleman.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "EsuEcosMiddleman.json"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "EsuEcosMiddleman", "EsuEcosMiddleman.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "EsuEcosMiddleman", "EsuEcosMiddleman.json")
            };

            foreach (var candidate in candidates)
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                    return full;
            }

            throw new FileNotFoundException("EsuEcosMiddleman.json not found in the app directory or project directory.", "EsuEcosMiddleman.json");
        }

        private static async Task Main()
        {
#if DEBUG
            Console.WriteLine("Attach debugger and enter any key...");
            Console.ReadKey();
#endif

            var loggerInstance = new Logger();

            var configPath = ResolveConfigPath();
            loggerInstance.Log?.Info($"Loading config from: {configPath}");

            var cfgCnt = File.ReadAllText(configPath, Encoding.UTF8);
            Cfg = JsonConvert.DeserializeObject<CfgRuntime>(cfgCnt);
            Cfg.Logger = loggerInstance;

            var mqttCfg = Cfg?.Extensions?.Actions?.Mqtt;
            loggerInstance.Log?.Info($"[Startup] MQTT broker configured: enabled={mqttCfg?.Enabled ?? false}, host={mqttCfg?.Host ?? "<n/a>"}, port={mqttCfg?.Port ?? -1}, clientId={mqttCfg?.ClientId ?? "<n/a>"}");

            var accessoryIds = (Cfg?.Extensions?.Actions?.Mappings ?? new System.Collections.Generic.List<CfgActionMapping>())
                .Where(m => m?.Trigger != null && m.Trigger.Address > 0)
                .Select(m => m.Trigger.Address)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            if (accessoryIds.Count > 0)
            {
                var idsText = string.Join(", ", accessoryIds);
                loggerInstance.Log?.Info($"[Startup] Accessory triggers registered for lamp control: {idsText}");

                foreach (var mapping in Cfg?.Extensions?.Actions?.Mappings ?? new System.Collections.Generic.List<CfgActionMapping>())
                {
                    if (mapping == null || mapping.Trigger == null) continue;
                    var topic = mapping.Action?.Topic ?? "<not configured>";
                    loggerInstance.Log?.Info($"[Startup] Accessory {mapping.Trigger.Address}: name='{mapping.Name}', topic='{topic}', state={mapping.Trigger.State ?? "<any>"}");
                }
            }
            else
            {
                loggerInstance.Log?.Info("[Startup] No accessory triggers registered for lamp control.");
            }

            var middleman = Middleman.Instance(Cfg);
            var startupOk = await middleman.RunAsync();
            if (!startupOk)
            {
                loggerInstance.Log?.Warn("Startup terminated because required local ports are unavailable. Check running proxy instances and configured listen port.");
                return;
            }

            loggerInstance.Log?.Info($"Started {DateTime.Now:F}");

            Console.WriteLine("Enter any key to quit...");
            while (Console.ReadKey().Key != ConsoleKey.Q) 
                Console.WriteLine("Press 'Q' to quit the application.");

            middleman.Stop();
        }
    }
}
