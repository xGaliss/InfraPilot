namespace InfraPilot.Agent.Core.HostedServices;

using Microsoft.Extensions.Hosting;

public sealed class AgentEnrollmentHostedService : IHostedService
{
    private readonly AgentRuntimeCoordinator _runtimeCoordinator;

    public AgentEnrollmentHostedService(AgentRuntimeCoordinator runtimeCoordinator)
    {
        _runtimeCoordinator = runtimeCoordinator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _runtimeCoordinator.EnsureRegisteredAsync(cancellationToken);
        await _runtimeCoordinator.PublishCapabilitiesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
