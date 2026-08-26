// Copyright (c) 2024 Dr. Christian Benjamin Ries
// Licensed under the MIT License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EsuEcosMiddleman.ECoS;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace EsuEcosMiddleman.Extensions
{
    public interface IAction
    {
        Task ExecuteAsync(ActionContext context);
    }

    public class ActionContext
    {
        public string MappingName { get; set; }
        public string RawCommand { get; set; }
        public ICommand Command { get; set; }
        public ActionTrigger Trigger { get; set; }
        public string ActionType { get; set; }
        public object Payload { get; set; }
    }

    public class ActionTrigger
    {
        public int Address { get; set; }
        public int? Port { get; set; }
        public string State { get; set; }

        public bool Matches(ActionTrigger other)
        {
            if (other == null) return false;
            if (Address != other.Address) return false;
            if (Port.HasValue && other.Port.HasValue && Port.Value != other.Port.Value) return false;
            if (Port.HasValue && !other.Port.HasValue) return false;
            if (string.IsNullOrWhiteSpace(State)) return true;
            if (string.IsNullOrWhiteSpace(other.State)) return false;
            return string.Equals(NormalizeState(State), NormalizeState(other.State), StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeState(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var v = value.Trim();
            if (int.TryParse(v, out var iv))
                return iv == 0 ? "off" : "on";
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return "on";
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return "off";
            if (v.Equals("on", StringComparison.OrdinalIgnoreCase)) return "on";
            if (v.Equals("off", StringComparison.OrdinalIgnoreCase)) return "off";
            return v.ToLowerInvariant();
        }
    }

    public static class ActionTriggerNormalizer
    {
        public static bool TryCreate(ICommand command, out ActionTrigger trigger)
        {
            trigger = null;
            if (command == null || command.ObjectId == -1)
                return false;

            var objId = command.ObjectId;
            var port = TryReadPort(command);
            var state = TryReadState(command);

            trigger = new ActionTrigger
            {
                Address = objId,
                Port = port,
                State = state
            };

            return true;
        }

        private static int? TryReadPort(ICommand command)
        {
            if (command == null || command.Arguments == null) return null;

            foreach (var arg in command.Arguments)
            {
                if (arg == null || string.IsNullOrWhiteSpace(arg.Name)) continue;
                if (arg.Name.Equals("port", StringComparison.OrdinalIgnoreCase)
                    || arg.Name.Equals("output", StringComparison.OrdinalIgnoreCase)
                    || arg.Name.Equals("channel", StringComparison.OrdinalIgnoreCase))
                {
                    if (arg.Parameter != null && arg.Parameter.Count > 0 && int.TryParse(arg.Parameter[0], out var p))
                        return p;
                }
            }

            foreach (var arg in command.Arguments)
            {
                if (arg == null || arg.Parameter == null || arg.Parameter.Count == 0) continue;
                if (arg.Name.Equals("state", StringComparison.OrdinalIgnoreCase) && int.TryParse(arg.Parameter[0], out var stateValue))
                    return stateValue;
            }

            return null;
        }

        private static string TryReadState(ICommand command)
        {
            if (command == null || command.Arguments == null) return null;

            foreach (var arg in command.Arguments)
            {
                if (arg == null || string.IsNullOrWhiteSpace(arg.Name)) continue;
                if (!arg.Name.Equals("state", StringComparison.OrdinalIgnoreCase)) continue;
                if (arg.Parameter == null || arg.Parameter.Count == 0) continue;
                var value = arg.Parameter[0];
                if (string.IsNullOrWhiteSpace(value)) continue;
                return ActionTrigger.NormalizeState(value);
            }

            return null;
        }
    }

    public class ActionMapping
    {
        public string Name { get; set; }
        public ActionTrigger Trigger { get; set; } = new ActionTrigger();
        public IAction Action { get; set; }
    }

    public interface IActionDispatcher
    {
        Task DispatchAsync(ICommand command, string rawCommand);
    }

    public interface ILightAutomationController
    {
        bool IsActive { get; }
        Task StartAsync(string reason);
        Task StopAsync(bool applyStopAction, string reason);
        Task TryStopOnManualOverrideAsync(ActionTrigger trigger);
    }

    public sealed class ActionDispatcher : IActionDispatcher
    {
        private readonly List<ActionMapping> _mappings;
        private readonly ILogger _logger;
        private readonly ILightAutomationController _lightAutomationController;

        public ActionDispatcher(IEnumerable<ActionMapping> mappings, ILogger logger = null, ILightAutomationController lightAutomationController = null)
        {
            _logger = logger;
            _mappings = mappings?.Where(m => m != null).ToList() ?? new List<ActionMapping>();
            _lightAutomationController = lightAutomationController;
        }

        public async Task DispatchAsync(ICommand command, string rawCommand)
        {
            if (command == null) return;
            if (!ActionTriggerNormalizer.TryCreate(command, out var trigger)) return;

            if (_lightAutomationController != null)
                await _lightAutomationController.TryStopOnManualOverrideAsync(trigger).ConfigureAwait(false);

            foreach (var mapping in _mappings)
            {
                if (mapping == null || mapping.Trigger == null || mapping.Action == null) continue;
                if (!mapping.Trigger.Matches(trigger)) continue;

                var context = new ActionContext
                {
                    MappingName = mapping.Name,
                    RawCommand = rawCommand,
                    Command = command,
                    Trigger = trigger,
                    ActionType = mapping.Action.GetType().Name
                };

                try
                {
                    _logger?.Log?.Info($"Action mapping matched: {mapping.Name} [DCC {trigger.Address}] ");
                    await mapping.Action.ExecuteAsync(context);
                }
                catch (Exception ex)
                {
                    _logger?.Log?.Warn($"Action execution failed: {mapping.Name} ({ex.Message})");
                }
            }
        }
    }

    internal static class ActionDispatcherFactory
    {
        public static ActionDispatcher Create(ICfgRuntime cfgRuntime, ILogger logger = null)
        {
            if (cfgRuntime?.Extensions?.Actions == null)
                return new ActionDispatcher(Array.Empty<ActionMapping>(), logger);

            var mappings = new List<ActionMapping>();
            var actionsCfg = cfgRuntime.Extensions.Actions;
            var lightAutomationController = LightAutomationControllerFactory.Create(actionsCfg, logger);
            if (!actionsCfg.Enabled)
                return new ActionDispatcher(mappings, logger, lightAutomationController);

            foreach (var mappingCfg in actionsCfg.Mappings ?? new List<CfgActionMapping>())
            {
                if (mappingCfg == null) continue;
                var action = ActionFactory.Create(mappingCfg.Action, actionsCfg, logger, lightAutomationController);
                if (action == null) continue;

                var trigger = new ActionTrigger
                {
                    Address = mappingCfg.Trigger?.Address ?? -1,
                    Port = mappingCfg.Trigger?.Port,
                    State = ActionTrigger.NormalizeState(mappingCfg.Trigger?.State ?? string.Empty)
                };

                if (trigger.Address == -1) continue;

                mappings.Add(new ActionMapping
                {
                    Name = mappingCfg.Name,
                    Trigger = trigger,
                    Action = action
                });
            }

            return new ActionDispatcher(mappings, logger, lightAutomationController);
        }
    }

    public static class ActionFactory
    {
        public static IAction Create(CfgActionDefinition cfgAction, CfgActions actionsCfg, ILogger logger = null, ILightAutomationController lightAutomationController = null)
        {
            if (cfgAction == null || actionsCfg == null) return null;

            switch ((cfgAction.Type ?? string.Empty).Trim())
            {
                case "mqtt":
                    if (!actionsCfg.Mqtt.Enabled) return null;
                    return new MqttAction(actionsCfg.Mqtt, logger, cfgAction.Topic, cfgAction.Payload);
                case "light-automation":
                    return new LightAutomationAction(lightAutomationController, LightAutomationPayloadParser.ReadMode(cfgAction.Payload), logger);
                default:
                    logger?.Log?.Warn($"Unsupported action type: {cfgAction.Type}");
                    return null;
            }
        }
    }

    internal static class LightAutomationPayloadParser
    {
        public static string ReadMode(object payload)
        {
            if (payload == null)
                return "start";

            if (payload is string p)
                return NormalizeMode(p);

            if (payload is JObject o)
                return NormalizeMode(o["mode"]?.ToString());

            return "start";
        }

        private static string NormalizeMode(string mode)
        {
            var value = (mode ?? string.Empty).Trim();
            return string.Equals(value, "stop", StringComparison.OrdinalIgnoreCase) ? "stop" : "start";
        }
    }

    public class LightAutomationAction : IAction
    {
        private readonly ILightAutomationController _controller;
        private readonly string _mode;
        private readonly ILogger _logger;

        public LightAutomationAction(ILightAutomationController controller, string mode, ILogger logger)
        {
            _controller = controller;
            _mode = string.IsNullOrWhiteSpace(mode) ? "start" : mode.Trim().ToLowerInvariant();
            _logger = logger;
        }

        public async Task ExecuteAsync(ActionContext context)
        {
            if (_controller == null)
            {
                _logger?.Log?.Warn("[LightAutomation] Controller unavailable; action skipped.");
                return;
            }

            if (_mode == "stop")
            {
                await _controller.StopAsync(true, $"switch-address={context?.Trigger?.Address ?? -1}").ConfigureAwait(false);
                return;
            }

            await _controller.StartAsync($"switch-address={context?.Trigger?.Address ?? -1}").ConfigureAwait(false);
        }
    }

    internal static class LightAutomationControllerFactory
    {
        public static ILightAutomationController Create(CfgActions actionsCfg, ILogger logger)
        {
            if (actionsCfg?.LightAutomation == null)
                return null;

            return new LightAutomationController(actionsCfg.LightAutomation, actionsCfg.Mqtt, logger);
        }
    }

    public sealed class LightAutomationController : ILightAutomationController
    {
        private readonly object _sync = new object();
        private readonly CfgLightAutomation _cfg;
        private readonly ILogger _logger;
        private readonly IMqttPublisher _publisher;
        private CancellationTokenSource _cts;
        private Task _task;

        public LightAutomationController(CfgLightAutomation cfg, CfgMqtt mqttCfg, ILogger logger)
        {
            _cfg = cfg ?? new CfgLightAutomation();
            _logger = logger;
            _publisher = new MqttPublisher(mqttCfg ?? new CfgMqtt(), logger);
        }

        public bool IsActive
        {
            get
            {
                lock (_sync)
                    return _task != null && !_task.IsCompleted;
            }
        }

        public async Task StartAsync(string reason)
        {
            if (!_cfg.Enabled)
            {
                _logger?.Log?.Info("[LightAutomation] Ignored start request because automation is disabled.");
                return;
            }

            var phases = GetValidPhases();
            if (phases.Count == 0)
            {
                _logger?.Log?.Warn("[LightAutomation] Ignored start request because no valid automation phases are configured.");
                return;
            }

            await StopAsync(false, "restart-before-start").ConfigureAwait(false);

            var tokenSource = new CancellationTokenSource();
            lock (_sync)
            {
                _cts = tokenSource;
                _task = RunSequenceAsync(phases, tokenSource.Token);
            }

            _logger?.Log?.Info($"[LightAutomation] Started (reason={reason ?? "n/a"}, durationMinutes={_cfg.DayDurationMinutes}).");
        }

        public async Task StopAsync(bool applyStopAction, string reason)
        {
            CancellationTokenSource tokenSource;
            Task runningTask;

            lock (_sync)
            {
                tokenSource = _cts;
                runningTask = _task;
                _cts = null;
                _task = null;
            }

            tokenSource?.Cancel();

            if (runningTask != null)
            {
                try
                {
                    await runningTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected while stopping automation
                }
            }

            if (applyStopAction)
                await PublishActionAsync(_cfg.StopAction, "stopAction", CancellationToken.None).ConfigureAwait(false);

            _logger?.Log?.Info($"[LightAutomation] Stopped (reason={reason ?? "n/a"}, applyStopAction={applyStopAction}).");
        }

        public async Task TryStopOnManualOverrideAsync(ActionTrigger trigger)
        {
            if (!IsActive)
                return;

            if (!_cfg.Enabled || trigger == null)
                return;

            if (trigger.Address == _cfg.SwitchAddress)
                return;

            var hasOverrides = _cfg.ManualOverrideAddresses != null && _cfg.ManualOverrideAddresses.Count > 0;
            if (!hasOverrides)
                return;

            if (!_cfg.ManualOverrideAddresses.Contains(trigger.Address))
                return;

            _logger?.Log?.Info($"[LightAutomation] Manual override detected by address {trigger.Address}; cancel running automation.");
            await StopAsync(false, $"manual-override-address={trigger.Address}").ConfigureAwait(false);
        }

        private List<CfgLightAutomationPhase> GetValidPhases()
        {
            return (_cfg.Phases ?? new List<CfgLightAutomationPhase>())
                .Where(p => p?.Action != null
                            && string.Equals((p.Action.Type ?? string.Empty).Trim(), "mqtt", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(p.Action.Topic))
                .OrderBy(p => p.AtPercent)
                .ToList();
        }

        private async Task RunSequenceAsync(List<CfgLightAutomationPhase> phases, CancellationToken token)
        {
            var durationMinutes = _cfg.DayDurationMinutes;
            if (durationMinutes == 0) durationMinutes = 15;
            var totalDurationMs = (long)durationMinutes * 60_000L;
            var cyclePauseSeconds = _cfg.CyclePauseSeconds;

            var cycle = 0UL;
            while (!token.IsCancellationRequested)
            {
                ++cycle;
                var startedAt = DateTime.UtcNow;

                foreach (var phase in phases)
                {
                    token.ThrowIfCancellationRequested();

                    var clampedPercent = Math.Min(100u, phase.AtPercent);
                    var dueMs = totalDurationMs * clampedPercent / 100L;
                    var elapsedMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
                    var waitMs = dueMs - elapsedMs;
                    if (waitMs > 0)
                        await Task.Delay(TimeSpan.FromMilliseconds(waitMs), token).ConfigureAwait(false);

                    await PublishActionAsync(phase.Action, phase.Name, token).ConfigureAwait(false);
                }

                _logger?.Log?.Info($"[LightAutomation] Cycle {cycle} completed; continuing with next cycle.");

                if (cyclePauseSeconds > 0)
                {
                    _logger?.Log?.Info($"[LightAutomation] Waiting {cyclePauseSeconds} seconds before next cycle.");
                    await Task.Delay(TimeSpan.FromSeconds(cyclePauseSeconds), token).ConfigureAwait(false);
                }
            }
        }

        private async Task PublishActionAsync(CfgActionDefinition action, string phaseName, CancellationToken token)
        {
            if (action == null)
                return;

            if (!string.Equals((action.Type ?? string.Empty).Trim(), "mqtt", StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(action.Topic))
                return;

            var payload = action.Payload ?? string.Empty;
            _logger?.Log?.Info($"[LightAutomation] Phase '{phaseName ?? "n/a"}' publish to topic '{action.Topic}'.");
            await _publisher.PublishAsync(action.Topic, payload).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
    }

    public class MqttAction : IAction
    {
        private readonly CfgMqtt _mqttConfig;
        private readonly ILogger _logger;
        private readonly string _topic;
        private readonly object _payload;
        private readonly IMqttPublisher _publisher;

        public MqttAction(CfgMqtt mqttConfig, ILogger logger, string topic, object payload)
        {
            _mqttConfig = mqttConfig;
            _logger = logger;
            _topic = topic;
            _payload = payload;
            _publisher = new MqttPublisher(mqttConfig, logger);
        }

        public async Task ExecuteAsync(ActionContext context)
        {
            if (_mqttConfig == null || ! _mqttConfig.Enabled)
            {
                _logger?.Log?.Warn("[MQTT] Action skipped because MQTT is disabled.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_topic))
            {
                _logger?.Log?.Warn("[MQTT] Action skipped because no MQTT topic is configured.");
                return;
            }

            var payload = GetPayload(context);
            _logger?.Log?.Info($"[MQTT] Trigger matched for mapping '{context?.MappingName ?? "<unnamed>"}': address={context?.Trigger?.Address ?? -1}, port={context?.Trigger?.Port ?? -1}, state={context?.Trigger?.State ?? "<none>"}; topic='{_topic}'");
            _logger?.Log?.Info($"[MQTT] Publishing payload to topic '{_topic}': {JsonConvert.SerializeObject(payload)}");
            await _publisher.PublishAsync(_topic, payload).ConfigureAwait(false);
        }

        private object GetPayload(ActionContext context)
        {
            if (_payload != null)
                return _payload;

            if (context?.Trigger == null)
                return new { };

            return new
            {
                address = context.Trigger.Address,
                port = context.Trigger.Port,
                state = context.Trigger.State
            };
        }
    }

    public interface IMqttPublisher : IDisposable
    {
        Task PublishAsync(string topic, object payload);
        bool IsConnected { get; }
    }

    public sealed class MqttPublisher : IMqttPublisher
    {
        private readonly object _sync = new object();
        private readonly CfgMqtt _config;
        private readonly ILogger _logger;
        private IMqttClient _client;

        public MqttPublisher(CfgMqtt config, ILogger logger)
        {
            _config = config ?? new CfgMqtt();
            _logger = logger;
        }

        public bool IsConnected => _client != null && _client.IsConnected;

        public async Task PublishAsync(string topic, object payload)
        {
            if (_config == null || !_config.Enabled || string.IsNullOrWhiteSpace(topic))
                return;

            try
            {
                await EnsureConnectedAsync().ConfigureAwait(false);
                var rawPayload = payload == null ? string.Empty : payload is string s ? s : JsonConvert.SerializeObject(payload);
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(topic)
                    .WithPayload(rawPayload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                    .WithRetainFlag(false)
                    .Build();

                _logger?.Log?.Info($"[MQTT] Sending to broker '{_config.Host}:{_config.Port}' on topic '{topic}' payload='{rawPayload}'");
                await _client.PublishAsync(message, CancellationToken.None).ConfigureAwait(false);
                _logger?.Log?.Info($"[MQTT] Publish succeeded: topic='{topic}', payload='{rawPayload}'");
            }
            catch (Exception ex)
            {
                _logger?.Log?.Warn($"[MQTT] Publish failed: topic='{topic}' error='{ex.Message}'");
            }
        }

        private async Task EnsureConnectedAsync()
        {
            if (_client != null && _client.IsConnected)
                return;

            lock (_sync)
            {
                if (_client != null && _client.IsConnected)
                    return;
            }

            _logger?.Log?.Info($"[MQTT] Connecting to broker '{_config.Host}:{_config.Port}' with clientId='{_config.ClientId}'");

            var mqttFactory = new MqttFactory();
            _client = mqttFactory.CreateMqttClient();
            _client.ConnectedAsync += async _ =>
            {
                _logger?.Log?.Info($"[MQTT] Connected to broker '{_config.Host}:{_config.Port}'");
                await Task.CompletedTask;
            };
            _client.DisconnectedAsync += async _ =>
            {
                _logger?.Log?.Warn("[MQTT] Connection lost to broker");
                _logger?.Log?.Info("[MQTT] Reconnecting to broker");
                await Task.CompletedTask;
            };

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(_config.Host, _config.Port)
                .WithClientId(string.IsNullOrWhiteSpace(_config.ClientId) ? "EsuEcosMiddleman" : _config.ClientId)
                .WithCleanSession();

            if (!string.IsNullOrWhiteSpace(_config.Username))
                options = options.WithCredentials(_config.Username, _config.Password);

            try
            {
                await _client.ConnectAsync(options.Build(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Log?.Warn($"[MQTT] Connection failed to '{_config.Host}:{_config.Port}' error='{ex.Message}'");
                throw;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_client == null) return;

                try
                {
                    _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None)
                        .GetAwaiter().GetResult();
                }
                catch
                {
                    // Swallow shutdown issues; the proxy must continue.
                }

                _client = null;
            }
        }
    }
}
