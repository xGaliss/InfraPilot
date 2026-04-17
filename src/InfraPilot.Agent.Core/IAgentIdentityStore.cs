namespace InfraPilot.Agent.Core;

public interface IAgentIdentityStore
{
    Task<AgentIdentity> GetOrCreateAsync(CancellationToken cancellationToken);

    Task SaveAsync(AgentIdentity identity, CancellationToken cancellationToken);
}
