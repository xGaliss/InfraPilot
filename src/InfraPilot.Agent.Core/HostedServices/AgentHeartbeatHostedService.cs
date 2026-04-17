namespace InfraPilot.Agent.Core.HostedServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AgentHeartbeatHostedService : AgentTaskHostedService
{
    private readonly AgentRuntimeCoordinator _runtimeCoordinator;
    private readonly AgentOptions _options;

    public AgentHeartbeatHostedService(
        AgentRuntimeCoordinator runtimeCoordinator,
        IOptions<AgentOptions> options,
        ILogger<AgentHeartbeatHostedService> logger)
        : base(logger)
    {
        _runtimeCoordinator = runtimeCoordinator;
        _options = options.Value;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(10, _options.HeartbeatIntervalSeconds));

    protected override Task ExecuteIterationAsync(CancellationToken stoppingToken)
        => _runtimeCoordinator.SendHeartbeatAsync(stoppingToken);
}
