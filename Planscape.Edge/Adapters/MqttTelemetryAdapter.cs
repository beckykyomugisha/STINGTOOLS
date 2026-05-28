using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using Newtonsoft.Json.Linq;
using Planscape.Core.Interfaces;

namespace Planscape.Edge.Adapters;

/// <summary>
/// 5C — MQTT transport (MQTTnet 4.x). Subscribes to the configured topic and
/// decodes JSON payloads into readings. Accepts a single object or an array:
///   { "deviceId":"AHU-01", "metric":"supply_air_temp_c", "value":23.4, "unit":"°C" }
/// deviceId may also be supplied by the topic ("planscape/&lt;deviceId&gt;/telemetry")
/// when absent from the payload.
/// </summary>
public sealed class MqttTelemetryAdapter : ITelemetryAdapter
{
    private readonly EdgeOptions _opt;
    private readonly ILogger<MqttTelemetryAdapter> _log;
    private IMqttClient? _client;

    public string Protocol => "mqtt";

    public MqttTelemetryAdapter(IOptions<EdgeOptions> opt, ILogger<MqttTelemetryAdapter> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public async Task StartAsync(Func<TelemetryBatch, Task> onBatch, CancellationToken ct)
    {
        var cfg = _opt.Adapters.Mqtt;
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(cfg.Host, cfg.Port)
            .WithCleanSession();
        if (!string.IsNullOrEmpty(cfg.Username))
            builder = builder.WithCredentials(cfg.Username, cfg.Password);
        var options = builder.Build();

        _client.ApplicationMessageReceivedAsync += async e =>
        {
            try
            {
                var topic = e.ApplicationMessage.Topic;
                var json = e.ApplicationMessage.ConvertPayloadToString();
                var readings = Decode(topic, json);
                if (readings.Count > 0)
                    await onBatch(new TelemetryBatch(_opt.ProjectId, readings));
            }
            catch (Exception ex) { _log.LogWarning(ex, "mqtt decode failed"); }
        };

        await _client.ConnectAsync(options, ct);
        await _client.SubscribeAsync(cfg.Topic, cancellationToken: ct);
        _log.LogInformation("MQTT connected {Host}:{Port}, subscribed {Topic}", cfg.Host, cfg.Port, cfg.Topic);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_client is { IsConnected: true })
            await _client.DisconnectAsync(cancellationToken: ct);
        _client?.Dispose();
        _client = null;
    }

    private static List<TelemetryReading> Decode(string topic, string payload)
    {
        var list = new List<TelemetryReading>();
        var token = JToken.Parse(payload);
        var items = token is JArray arr ? arr.Children() : new[] { token };
        var topicDevice = TopicDevice(topic);

        foreach (var it in items)
        {
            var o = it as JObject;
            if (o == null) continue;
            var deviceId = (string?)o["deviceId"] ?? topicDevice;
            var metric = (string?)o["metric"];
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(metric)) continue;
            var value = (double?)o["value"] ?? 0;
            var unit = (string?)o["unit"];
            var ts = (DateTime?)o["ts"];
            list.Add(new TelemetryReading(deviceId!, metric!, value, unit, ts));
        }
        return list;
    }

    private static string? TopicDevice(string topic)
    {
        // planscape/<deviceId>/telemetry → deviceId
        var parts = topic.Split('/');
        return parts.Length >= 2 ? parts[1] : null;
    }
}
