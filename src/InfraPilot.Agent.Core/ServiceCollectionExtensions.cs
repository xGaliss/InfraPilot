namespace InfraPilot.Agent.Core;

using InfraPilot.Agent.Core.HostedServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfraPilotAgentCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection(AgentOptions.SectionName));
        services.AddSingleton<IAgentIdentityStore, FileAgentIdentityStore>();
        services.AddHttpClient<ICentralAgentApiClient, CentralAgentApiClient>();
        services.AddSingleton<AgentRuntimeCoordinator>();

        services.AddHostedService<AgentEnrollmentHostedService>();
        services.AddHostedService<AgentHeartbeatHostedService>();
        services.AddHostedService<AgentSnapshotHostedService>();
        services.AddHostedService<AgentActionPollingHostedService>();

        return services;
    }
}
