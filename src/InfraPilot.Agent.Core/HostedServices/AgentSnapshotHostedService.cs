namespace InfraPilot.Agent.Core.HostedServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AgentSnapshotHostedService : AgentTaskHostedService
{
    private readonly AgentRuntimeCoordinator _runtimeCoordinator;
    private readonly AgentOptions _options;

    public AgentSnapshotHostedService(
        AgentRuntimeCoordinator runtimeCoordinator,
        IOptions<AgentOptions> options,
        ILogger<AgentSnapshotHostedService> logger)
        : base(logger)
    {
        _runtimeCoordinator = runtimeCoordinator;
        _options = options.Value;
    }

    protected override TimeSpan Interval => TimeSpan.FromSeconds(Math.Max(15, _options.SnapshotIntervalSeconds));

    protected override Task ExecuteIterationAsync(CancellationToken stoppingToken)
        => _runtimeCoordinator.PublishSnapshotsAsync(stoppingToken);
}
