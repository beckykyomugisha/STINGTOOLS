using Planscape.Core.Interfaces;
using Planscape.Edge;
using Planscape.Edge.Adapters;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<EdgeOptions>(builder.Configuration.GetSection("Edge"));

// Durable queue + HTTP forwarder.
builder.Services.AddSingleton(sp =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EdgeOptions>>().Value;
    return new StoreAndForwardQueue(opt.QueuePath);
});
builder.Services.AddHttpClient<ServerForwarder>();

// Protocol adapters — all registered; the Worker starts only those enabled in
// config. Adapters reuse the server's ITelemetryAdapter contract.
builder.Services.AddSingleton<ITelemetryAdapter, SimulatorTelemetryAdapter>();
builder.Services.AddSingleton<ITelemetryAdapter, MqttTelemetryAdapter>();
builder.Services.AddSingleton<ITelemetryAdapter, ModbusTelemetryAdapter>();
builder.Services.AddSingleton<ITelemetryAdapter, BacnetTelemetryAdapter>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
