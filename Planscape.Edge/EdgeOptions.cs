namespace Planscape.Edge;

/// <summary>Edge agent configuration (bound from the "Edge" config section).</summary>
public sealed class EdgeOptions
{
    public string ServerUrl { get; set; } = "http://localhost:5000";
    public Guid ProjectId { get; set; }
    public string ApiToken { get; set; } = "";
    public string QueuePath { get; set; } = "./queue";
    public int ForwardIntervalMs { get; set; } = 2000;
    public int ForwardBatchSize { get; set; } = 500;
    public AdaptersOptions Adapters { get; set; } = new();
}

public sealed class AdaptersOptions
{
    public SimulatorOptions Simulator { get; set; } = new();
    public MqttOptions Mqtt { get; set; } = new();
    public ToggleOptions Modbus { get; set; } = new();
    public ToggleOptions Bacnet { get; set; } = new();
}

public class ToggleOptions { public bool Enabled { get; set; } }

public sealed class SimulatorOptions : ToggleOptions
{
    public List<string> Devices { get; set; } = new();
    public List<string> Metrics { get; set; } = new();
    public int IntervalMs { get; set; } = 5000;
}

public sealed class MqttOptions : ToggleOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string Topic { get; set; } = "planscape/+/telemetry";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
